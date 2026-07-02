using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using JarvisCSharp.Core;
using WindowsInput;
using WindowsInput.Native;

namespace JarvisCSharp.Services.Automation
{
    public class AutomationControllerService : IAutomationControllerService
    {
        private readonly InputSimulator _simulator;

        public AutomationControllerService()
        {
            _simulator = new InputSimulator();
        }

        // --- Keyboard Operations ---
        public async Task<ExecutionResult> TypeTextAsync(string text, Point? coordinate = null, bool clearExisting = true)
        {
            try
            {
                if (coordinate.HasValue)
                {
                    await ClickAsync(coordinate.Value, ClickType.LeftSingle, false);
                    await Task.Delay(100);
                }

                if (clearExisting)
                {
                    _simulator.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);
                    await Task.Delay(50);
                    _simulator.Keyboard.KeyPress(VirtualKeyCode.BACK);
                    await Task.Delay(50);
                }

                _simulator.Keyboard.TextEntry(text);
                
                return new ExecutionResult { Success = true, Message = $"Typed text '{text}'" };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "TypeTextAsync failed");
                return new ExecutionResult { Success = false, Error = ex, Message = ex.Message };
            }
        }

        public async Task<ExecutionResult> SendShortcutAsync(string shortcut, IntPtr? targetWindow = null)
        {
            try
            {
                if (targetWindow.HasValue && targetWindow.Value != IntPtr.Zero)
                {
                    await BringWindowToFocusAsync(targetWindow.Value);
                    await Task.Delay(100);
                }

                // parse shortcut string (e.g., "Ctrl+S")
                var keys = shortcut.Split(new[] { '+', '-' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(k => k.Trim().ToLower())
                                   .ToArray();

                var modifiers = new List<VirtualKeyCode>();
                var mainKeys = new List<VirtualKeyCode>();

                foreach (var k in keys)
                {
                    switch (k)
                    {
                        case "ctrl":
                        case "control":
                            modifiers.Add(VirtualKeyCode.CONTROL);
                            break;
                        case "alt":
                            modifiers.Add(VirtualKeyCode.MENU);
                            break;
                        case "shift":
                            modifiers.Add(VirtualKeyCode.SHIFT);
                            break;
                        case "win":
                        case "windows":
                            modifiers.Add(VirtualKeyCode.LWIN);
                            break;
                        default:
                            if (Enum.TryParse<VirtualKeyCode>("VK_" + k.ToUpper(), out var vk))
                            {
                                mainKeys.Add(vk);
                            }
                            else if (k.Length == 1)
                            {
                                // simple char mapping
                                mainKeys.Add((VirtualKeyCode)k.ToUpper()[0]);
                            }
                            break;
                    }
                }

                if (modifiers.Count > 0)
                {
                    _simulator.Keyboard.ModifiedKeyStroke(modifiers, mainKeys);
                }
                else if (mainKeys.Count > 0)
                {
                    foreach(var mk in mainKeys) _simulator.Keyboard.KeyPress(mk);
                }

                return new ExecutionResult { Success = true, Message = $"Sent shortcut {shortcut}" };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "SendShortcutAsync failed");
                return new ExecutionResult { Success = false, Error = ex, Message = ex.Message };
            }
        }

        public async Task<ExecutionResult> SendKeySequenceAsync(string[] keys, int delayMs = 100)
        {
            try
            {
                foreach (var key in keys)
                {
                    await SendShortcutAsync(key);
                    await Task.Delay(delayMs);
                }
                return new ExecutionResult { Success = true, Message = "Sent key sequence" };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Error = ex, Message = ex.Message };
            }
        }

        // --- Mouse Operations ---
        public async Task<ExecutionResult> ClickAsync(Point screenCoordinate, ClickType type = ClickType.LeftSingle, bool ensureFocus = true)
        {
            try
            {
                SetCursorPos(screenCoordinate.X, screenCoordinate.Y);
                await Task.Delay(50); // let OS register movement

                switch (type)
                {
                    case ClickType.LeftSingle:
                        _simulator.Mouse.LeftButtonClick();
                        break;
                    case ClickType.LeftDouble:
                        _simulator.Mouse.LeftButtonDoubleClick();
                        break;
                    case ClickType.RightSingle:
                        _simulator.Mouse.RightButtonClick();
                        break;
                    case ClickType.MiddleSingle:
                        mouse_event(MOUSEEVENTF_MIDDLEDOWN | MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero);
                        break;
                }

                return new ExecutionResult { Success = true, Message = $"Clicked at {screenCoordinate}" };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ClickAsync failed");
                return new ExecutionResult { Success = false, Error = ex, Message = ex.Message };
            }
        }

        public async Task<ExecutionResult> DragAsync(Point start, Point end, int durationMs = 500)
        {
            try
            {
                SetCursorPos(start.X, start.Y);
                await Task.Delay(50);
                _simulator.Mouse.LeftButtonDown();
                await Task.Delay(50);

                int steps = Math.Max(1, durationMs / 10);
                for (int i = 1; i <= steps; i++)
                {
                    int x = start.X + (end.X - start.X) * i / steps;
                    int y = start.Y + (end.Y - start.Y) * i / steps;
                    SetCursorPos(x, y);
                    await Task.Delay(10);
                }

                _simulator.Mouse.LeftButtonUp();
                return new ExecutionResult { Success = true, Message = "Drag complete" };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Error = ex, Message = ex.Message };
            }
        }

        // --- Window Management ---
        private readonly Dictionary<string, string> _recentlyUsedApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public async Task<ExecutionResult> LaunchApplicationAsync(string appNameOrPath, int timeoutSeconds = 10)
        {
            try
            {
                string resolvedPath = ResolveApplicationPath(appNameOrPath);
                
                var psi = new ProcessStartInfo
                {
                    FileName = resolvedPath,
                    UseShellExecute = true
                };
                var process = Process.Start(psi);
                
                var startTime = DateTime.UtcNow;
                while (DateTime.UtcNow - startTime < TimeSpan.FromSeconds(timeoutSeconds))
                {
                    if (process != null)
                    {
                        process.Refresh();
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            // Track successfully launched app
                            _recentlyUsedApps[appNameOrPath] = resolvedPath;
                            return new ExecutionResult { Success = true, Message = $"Launched {resolvedPath}" };
                        }
                    }
                    await Task.Delay(500);
                }

                // If we reach here, it might have launched but not created a main window yet or we couldn't track it
                // We'll still track it in MRU if the process didn't exit immediately
                if (process != null && !process.HasExited)
                {
                    _recentlyUsedApps[appNameOrPath] = resolvedPath;
                    return new ExecutionResult { Success = true, Message = $"Launched {resolvedPath} (MainWindow not detected within timeout)" };
                }

                return new ExecutionResult { Success = false, Message = $"Timeout waiting for {appNameOrPath} window" };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Error = ex, Message = ex.Message };
            }
        }

        private string ResolveApplicationPath(string appNameOrPath)
        {
            // 1. Check if it's already an absolute path
            if (System.IO.Path.IsPathRooted(appNameOrPath) && System.IO.File.Exists(appNameOrPath))
            {
                return appNameOrPath;
            }

            // 2. Check recently used apps
            if (_recentlyUsedApps.TryGetValue(appNameOrPath, out var recentPath) && System.IO.File.Exists(recentPath))
            {
                return recentPath;
            }

            // 3. Prepare search names (with and without .exe)
            var searchNames = new List<string> { appNameOrPath };
            if (!appNameOrPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                searchNames.Insert(0, appNameOrPath + ".exe");
            }

            // 4. Check system PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                var paths = pathEnv.Split(System.IO.Path.PathSeparator);
                foreach (var path in paths)
                {
                    foreach (var name in searchNames)
                    {
                        try
                        {
                            var fullPath = System.IO.Path.Combine(path.Trim(), name);
                            if (System.IO.File.Exists(fullPath)) return fullPath;
                        }
                        catch { }
                    }
                }
            }

            // 5. Check Common Directories
            var commonDirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System)
            };

            // Limit depth for performance or just check immediate subdirectories
            // For a robust implementation, Windows Search API or reading start menu shortcuts is better,
            // but we'll try a basic approach: fallback to letting ShellExecute try to find it
            
            return appNameOrPath; // Let ShellExecute try resolving it natively
        }

        public async Task<ExecutionResult> BringWindowToFocusAsync(IntPtr windowHandle)
        {
            Win32Interop.BringToFront(windowHandle);
            return await Task.FromResult(new ExecutionResult { Success = true });
        }

        public async Task<ExecutionResult> MaximizeWindowAsync(IntPtr windowHandle)
        {
            Win32Interop.ShowWindow(windowHandle, Win32Interop.SW_SHOWMAXIMIZED);
            return await Task.FromResult(new ExecutionResult { Success = true });
        }

        public async Task<ExecutionResult> MinimizeWindowAsync(IntPtr windowHandle)
        {
            Win32Interop.ShowWindow(windowHandle, Win32Interop.SW_SHOWMINIMIZED);
            return await Task.FromResult(new ExecutionResult { Success = true });
        }

        public async Task<ExecutionResult> CloseWindowAsync(IntPtr windowHandle, bool force = false)
        {
            if (force)
            {
                uint processId;
                GetWindowThreadProcessId(windowHandle, out processId);
                var process = Process.GetProcessById((int)processId);
                process.Kill();
            }
            else
            {
                PostMessage(windowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            return await Task.FromResult(new ExecutionResult { Success = true });
        }

        public async Task<ExecutionResult> MoveWindowAsync(IntPtr windowHandle, WindowPosition position)
        {
            var monitors = GetMonitorConfiguration();
            var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
            if (primary == null) return new ExecutionResult { Success = false, Message = "No monitors found" };

            var workArea = primary.WorkingArea;
            int x = workArea.X, y = workArea.Y, w = workArea.Width, h = workArea.Height;

            switch (position)
            {
                case WindowPosition.Maximize:
                    return await MaximizeWindowAsync(windowHandle);
                case WindowPosition.Minimize:
                    return await MinimizeWindowAsync(windowHandle);
                case WindowPosition.LeftHalf:
                    w = workArea.Width / 2;
                    break;
                case WindowPosition.RightHalf:
                    x = workArea.X + workArea.Width / 2;
                    w = workArea.Width / 2;
                    break;
                case WindowPosition.TopHalf:
                    h = workArea.Height / 2;
                    break;
                case WindowPosition.BottomHalf:
                    y = workArea.Y + workArea.Height / 2;
                    h = workArea.Height / 2;
                    break;
                case WindowPosition.Center:
                    w = workArea.Width / 2;
                    h = workArea.Height / 2;
                    x = workArea.X + workArea.Width / 4;
                    y = workArea.Y + workArea.Height / 4;
                    break;
                case WindowPosition.Monitor1:
                case WindowPosition.Monitor2:
                    var targetMonitor = monitors.FirstOrDefault(m => m.Index == (position == WindowPosition.Monitor1 ? 0 : 1));
                    if (targetMonitor != null)
                    {
                        x = targetMonitor.WorkingArea.X;
                        y = targetMonitor.WorkingArea.Y;
                        w = targetMonitor.WorkingArea.Width;
                        h = targetMonitor.WorkingArea.Height;
                    }
                    break;
            }

            MoveWindow(windowHandle, x, y, w, h, true);
            return await Task.FromResult(new ExecutionResult { Success = true });
        }

        // --- Display & Coordinates ---
        public List<MonitorInfo> GetMonitorConfiguration()
        {
            var list = new List<MonitorInfo>();
            // Fallback since WinForms is not available, we can use an empty implementation or add WinForms
            // To make this robust without WinForms, we use P/Invoke EnumDisplayMonitors
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
                {
                    list.Add(new MonitorInfo
                    {
                        Index = list.Count,
                        Bounds = new Rectangle(lprcMonitor.left, lprcMonitor.top, lprcMonitor.right - lprcMonitor.left, lprcMonitor.bottom - lprcMonitor.top),
                        WorkingArea = new Rectangle(lprcMonitor.left, lprcMonitor.top, lprcMonitor.right - lprcMonitor.left, lprcMonitor.bottom - lprcMonitor.top),
                        IsPrimary = (list.Count == 0), // Hack, primary is usually first
                        DpiScaleFactor = 1.0f
                    });
                    return true;
                }, IntPtr.Zero);

            return list;
        }

        public Point ApplyDpiScaling(Point coordinate, MonitorInfo monitor)
        {
            return new Point((int)(coordinate.X * monitor.DpiScaleFactor), (int)(coordinate.Y * monitor.DpiScaleFactor));
        }

        // PInvoke
        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError=true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        const uint WM_CLOSE = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    }
}
