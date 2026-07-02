using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    public class ContextManagerService : IContextManagerService, IDisposable
    {
        private readonly AutomationControllerService _automationController;
        private readonly ConcurrentDictionary<IntPtr, WindowInfo> _trackedWindows;
        private readonly List<InteractionRecord> _interactionHistory;
        private readonly Dictionary<string, WindowPreference> _preferences;
        
        private CancellationTokenSource _cts;
        private string _prefsFilePath;
        private readonly int _historyLimit = 20;
        private IntPtr _lastForegroundWindow = IntPtr.Zero;

        public event Action<WindowStateChange>? OnWindowStateChanged;

        public ContextManagerService(AutomationControllerService automationController)
        {
            _automationController = automationController;
            _trackedWindows = new ConcurrentDictionary<IntPtr, WindowInfo>();
            _interactionHistory = new List<InteractionRecord>();
            _preferences = new Dictionary<string, WindowPreference>(StringComparer.OrdinalIgnoreCase);
            
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JarvisCSharp");
            Directory.CreateDirectory(appData);
            _prefsFilePath = Path.Combine(appData, "window_preferences.json");
            
            LoadPreferences();

            _cts = new CancellationTokenSource();
            _ = PollWindowsAsync(_cts.Token);
        }

        public List<WindowInfo> GetAllWindows()
        {
            return _trackedWindows.Values.ToList();
        }

        public WindowInfo? ResolveCurrentWindow()
        {
            // The foreground window excluding Jarvis itself
            var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
            var foreground = Win32Interop.GetForegroundWindow();

            GetWindowThreadProcessId(foreground, out uint fgProcessId);
            if (fgProcessId != currentProcessId && _trackedWindows.TryGetValue(foreground, out var info))
            {
                return info;
            }

            // If Jarvis is focused, try the last foreground window before Jarvis
            if (fgProcessId == currentProcessId && _lastForegroundWindow != IntPtr.Zero && _trackedWindows.TryGetValue(_lastForegroundWindow, out var lastInfo))
            {
                return lastInfo;
            }

            // Fallback to highest z-order window
            return _trackedWindows.Values
                .Where(w => w.IsVisible)
                .OrderBy(w => w.ZOrder)
                .FirstOrDefault();
        }

        public WindowInfo? FindWindowByTitle(string titleQuery)
        {
            var windows = GetAllWindows();
            return windows.FirstOrDefault(w => w.Title.Equals(titleQuery, StringComparison.OrdinalIgnoreCase)) ??
                   windows.FirstOrDefault(w => w.Title.IndexOf(titleQuery, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public List<InteractionRecord> GetInteractionHistory(int count = 20)
        {
            lock (_interactionHistory)
            {
                return _interactionHistory.AsEnumerable().Reverse().Take(count).ToList();
            }
        }

        public void RecordInteraction(InteractionRecord interaction)
        {
            lock (_interactionHistory)
            {
                _interactionHistory.Add(interaction);
                if (_interactionHistory.Count > _historyLimit)
                {
                    _interactionHistory.RemoveAt(0);
                }
            }
        }

        public void SaveWindowPreference(string appName, WindowPreference preference)
        {
            lock (_preferences)
            {
                _preferences[appName] = preference;
                SavePreferences();
            }
        }

        public WindowPreference? GetWindowPreference(string appName)
        {
            lock (_preferences)
            {
                if (_preferences.TryGetValue(appName, out var pref)) return pref;
                return null;
            }
        }

        public MonitorInfo GetMonitorForWindow(IntPtr windowHandle)
        {
            var monitors = _automationController.GetMonitorConfiguration();
            if (_trackedWindows.TryGetValue(windowHandle, out var info))
            {
                // Find monitor that contains the center of the window
                var center = new Point(info.Bounds.X + info.Bounds.Width / 2, info.Bounds.Y + info.Bounds.Height / 2);
                foreach (var m in monitors)
                {
                    if (m.Bounds.Contains(center)) return m;
                }
            }
            return monitors.FirstOrDefault(m => m.IsPrimary) ?? new MonitorInfo();
        }

        public WindowInfo? DisambiguateTarget(string ambiguousRef, List<WindowInfo> candidates)
        {
            if (candidates.Count == 1) return candidates[0];
            if (candidates.Count == 0) return null;

            var history = GetInteractionHistory(10);
            foreach (var record in history)
            {
                var match = candidates.FirstOrDefault(c => c.Handle == record.TargetWindow.Handle);
                if (match != null) return match;
            }

            return candidates[0]; // fallback
        }

        private async Task PollWindowsAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    UpdateWindowsState();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "PollWindowsAsync error");
                }
                await Task.Delay(2000, ct);
            }
        }

        private void UpdateWindowsState()
        {
            var currentWindows = new Dictionary<IntPtr, WindowInfo>();
            int zOrder = 0;

            var foreground = Win32Interop.GetForegroundWindow();
            var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
            GetWindowThreadProcessId(foreground, out uint fgProcessId);
            if (fgProcessId != currentProcessId && foreground != IntPtr.Zero)
            {
                _lastForegroundWindow = foreground;
            }

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    GetWindowRect(hWnd, out var rect);
                    var bounds = new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
                    
                    if (bounds.Width > 0 && bounds.Height > 0)
                    {
                        var titleBuilder = new System.Text.StringBuilder(256);
                        GetWindowText(hWnd, titleBuilder, 256);
                        var title = titleBuilder.ToString();

                        if (!string.IsNullOrWhiteSpace(title) && title != "Program Manager")
                        {
                            var info = new WindowInfo
                            {
                                Handle = hWnd,
                                Title = title,
                                Bounds = bounds,
                                ZOrder = zOrder++,
                                IsVisible = true,
                                IsForeground = (hWnd == foreground)
                            };
                            currentWindows[hWnd] = info;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            // Compare with tracked windows to fire events
            foreach (var kvp in currentWindows)
            {
                if (!_trackedWindows.ContainsKey(kvp.Key))
                {
                    // Created
                    _trackedWindows[kvp.Key] = kvp.Value;
                    FireEvent(kvp.Key, WindowStateChangeType.Created, null, kvp.Value);
                }
                else
                {
                    var oldInfo = _trackedWindows[kvp.Key];
                    var newInfo = kvp.Value;
                    
                    if (oldInfo.Bounds.X != newInfo.Bounds.X || oldInfo.Bounds.Y != newInfo.Bounds.Y)
                        FireEvent(kvp.Key, WindowStateChangeType.Moved, oldInfo.Bounds, newInfo.Bounds);
                        
                    if (oldInfo.Bounds.Width != newInfo.Bounds.Width || oldInfo.Bounds.Height != newInfo.Bounds.Height)
                        FireEvent(kvp.Key, WindowStateChangeType.Resized, oldInfo.Bounds, newInfo.Bounds);

                    if (!oldInfo.IsForeground && newInfo.IsForeground)
                        FireEvent(kvp.Key, WindowStateChangeType.FocusChanged, false, true);

                    // Note: MonitorChanged can be checked if we assign Monitor to WindowInfo
                    _trackedWindows[kvp.Key] = newInfo;
                }
            }

            // Check for closed windows
            var closedHandles = _trackedWindows.Keys.Except(currentWindows.Keys).ToList();
            foreach (var handle in closedHandles)
            {
                if (_trackedWindows.TryRemove(handle, out var oldInfo))
                {
                    FireEvent(handle, WindowStateChangeType.Closed, oldInfo, null);
                }
            }
        }

        private void FireEvent(IntPtr handle, WindowStateChangeType type, object? oldVal, object? newVal)
        {
            OnWindowStateChanged?.Invoke(new WindowStateChange
            {
                WindowHandle = handle,
                ChangeType = type,
                OldValue = oldVal,
                NewValue = newVal,
                Timestamp = DateTime.UtcNow
            });
        }

        private void LoadPreferences()
        {
            try
            {
                if (File.Exists(_prefsFilePath))
                {
                    var json = File.ReadAllText(_prefsFilePath);
                    var prefs = JsonSerializer.Deserialize<Dictionary<string, WindowPreference>>(json);
                    if (prefs != null)
                    {
                        foreach (var kvp in prefs) _preferences[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "LoadPreferences failed");
            }
        }

        private void SavePreferences()
        {
            try
            {
                var json = JsonSerializer.Serialize(_preferences, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_prefsFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SavePreferences failed");
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        // PInvoke
        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError=true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }
    }
}
