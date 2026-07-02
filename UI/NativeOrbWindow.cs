using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using JarvisCSharp.Core;
using JarvisCSharp.Services;
using Microsoft.Web.WebView2.Core;

namespace JarvisCSharp.UI;

/// <summary>
/// Native Win32 floating orb overlay — zero WPF dependency.
///
/// The orb is a freely-positionable floating assistant. You can drag it
/// anywhere on screen. It remembers its position between sessions.
///
/// Two modes:
///   Compact  — just the orb (72x72), floats anywhere, always visible
///   Expanded  — orb + chat panel (440x600), opens when you click the orb
///
/// The window is created with these styles:
///   WS_POPUP              — no title bar, no border, no chrome
///   WS_EX_LAYERED         — per-pixel alpha transparency
///   WS_EX_NOACTIVATE      — never steals focus from the current app
///   WS_EX_TOOLWINDOW      — invisible to Alt+Tab, Task View, Win+Tab
///   WS_EX_TOPMOST         — always above all other windows
///   WS_EX_NOREDIRECTIONBITMAP — direct composition, no GDI surface
///
/// Dragging is handled via WM_NCHITTEST returning HTCAPTION when the
/// mouse is over the orb area (the web UI tells us via a bridge message
/// whether the pointer is in the drag zone).
/// </summary>
public sealed class NativeOrbWindow : IBridgeHost, IDisposable
{
    private const string ClassName = "JarvisOrbOverlay";

    // ── Win32 constants ───────────────────────────────────────
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;
    private const uint WS_CLIPCHILDREN = 0x02000000;

    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    private const int GWL_EXSTYLE = -20;

    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;

    private const int HWND_TOPMOST = -1;
    private const uint LWA_ALPHA = 0x02;
    private const int DWMWA_EXCLUDED_FROM_PEEK = 12;

    // Window messages
    private const int WM_DESTROY = 0x0002;
    private const int WM_SIZE = 0x0005;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_ESCAPE = 0x1B;

    // Hit test codes
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTTRANSPARENT = -1;

    // ShowWindow commands
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    // ── P/Invoke ──────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern short RegisterClassEx(ref WNDCLASSEX lpwcx);

    // ── MSG struct for message pump ───────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hWnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    // ── Sizes ─────────────────────────────────────────────────
    private const int CompactW = 80;
    private const int CompactH = 80;
    // Fullscreen mode uses the actual screen dimensions (computed at expand time)

    // ── State ─────────────────────────────────────────────────
    private IntPtr _hwnd;
    private IntPtr _hinstance;
    private WndProcDelegate? _wndProc;
    private CoreWebView2Environment? _env;
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _core;
    private readonly Bridge _bridge;
    private readonly Thread _messageThread;
    private bool _disposed;
    private bool _initialized;
    private bool _summonPending;
    private bool _isExpanded;
    private bool _isDragging;     // web UI says we're in the drag zone
    private int _currentX, _currentY;

    // Position persistence
    private static readonly string PositionFile =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jarvis", "orb_position.json");

    public event Action? Dismissed;
    public event Action? OpenOverlayRequested;
    public event Action<bool>? VoiceModeChanged;

    public NativeOrbWindow(SystemShellService shell, SystemInfoService sysInfo)
    {
        _bridge = new Bridge(this, shell, sysInfo);

        _messageThread = new Thread(MessageThreadProc)
        {
            Name = "JarvisOrb",
            IsBackground = true,
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();

        _createdEvent.WaitOne();
    }

    private readonly AutoResetEvent _createdEvent = new(false);

    // ── Position persistence ──────────────────────────────────

    private static (int x, int y) LoadPosition()
    {
        try
        {
            if (File.Exists(PositionFile))
            {
                var json = File.ReadAllText(PositionFile);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var x = doc.RootElement.GetProperty("x").GetInt32();
                var y = doc.RootElement.GetProperty("y").GetInt32();
                // Clamp to visible screen
                var screen = System.Windows.SystemParameters.WorkArea;
                x = Math.Clamp(x, 0, (int)screen.Width - CompactW);
                y = Math.Clamp(y, 0, (int)screen.Height - CompactH);
                return (x, y);
            }
        }
        catch { /* non-fatal */ }

        // Default: bottom-right corner
        var wa = System.Windows.SystemParameters.WorkArea;
        return ((int)wa.Width - CompactW - 24, (int)wa.Height - CompactH - 24);
    }

    private static void SavePosition(int x, int y)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { x, y });
            File.WriteAllText(PositionFile, json);
        }
        catch { /* non-fatal */ }
    }

    // ── Window creation + message pump ────────────────────────

    private void MessageThreadProc()
    {
        var hMod = GetModuleHandle(null!);
        _hinstance = hMod != IntPtr.Zero ? hMod : Marshal.GetHINSTANCE(typeof(NativeOrbWindow).Assembly.GetModules()[0]);

        _wndProc = WndProc;
        var wc = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _wndProc,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = _hinstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null!,
            lpszClassName = ClassName,
            hIconSm = IntPtr.Zero,
        };
        RegisterClassEx(ref wc);

        // Load saved position (or default to bottom-right)
        var (x, y) = LoadPosition();
        _currentX = x;
        _currentY = y;

        int exStyle = unchecked((int)(WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE |
                      WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP));
        int style = unchecked((int)(WS_POPUP | WS_CLIPSIBLINGS | WS_CLIPCHILDREN));

        // Start in compact mode
        _hwnd = CreateWindowEx(exStyle, ClassName, "", style,
            x, y, CompactW, CompactH,
            IntPtr.Zero, IntPtr.Zero, _hinstance, IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);

            int exclude = 1;
            DwmSetWindowAttribute(_hwnd, DWMWA_EXCLUDED_FROM_PEEK, ref exclude, sizeof(int));

            // Hide on startup — orb only appears when summoned via Win+J or voice
            ShowWindow(_hwnd, SW_HIDE);

            _ = InitializeWebViewAsync();
            _createdEvent.Set();

            // Message pump
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        else
        {
            _createdEvent.Set();
        }
    }

    private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            // ── Drag support: WM_NCHITTEST ───────────────────────
            // When the web UI says the mouse is in the drag zone (the orb
            // itself, not the chat panel), we return HTCAPTION so Win32
            // handles dragging natively. Otherwise return HTCLIENT so
            // WebView2 receives the clicks normally.
            case WM_NCHITTEST:
                if (_isDragging && !_isExpanded)
                {
                    return (IntPtr)HTCAPTION;
                }
                // In expanded mode, only the orb area (top portion) is draggable
                if (_isDragging && _isExpanded)
                {
                    return (IntPtr)HTCAPTION;
                }
                return (IntPtr)HTCLIENT;

            case WM_KEYDOWN:
                if (wParam.ToInt32() == VK_ESCAPE)
                {
                    if (_isExpanded) Collapse();
                    else Dismiss();
                }
                break;

            case WM_SIZE:
                if (_controller != null)
                {
                    int width = lParam.ToInt32() & 0xFFFF;
                    int height = (lParam.ToInt32() >> 16) & 0xFFFF;
                    _controller.Bounds = new System.Drawing.Rectangle(0, 0, width, height);
                }
                break;

            case WM_DESTROY:
                // Save position before exiting
                SavePosition(_currentX, _currentY);
                PostQuitMessage(0);
                break;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // ── WebView2 init ─────────────────────────────────────────
    private async Task InitializeWebViewAsync()
    {
        try
        {
            _env = await CoreWebView2Environment.CreateAsync(null, null, null);
            _controller = await _env.CreateCoreWebView2ControllerAsync(_hwnd, null);
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, CompactW, CompactH);
            _controller.DefaultBackgroundColor = System.Drawing.Color.Transparent;

            _core = _controller.CoreWebView2;

            var webRoot = ExtractOrbAssets();
            _core.SetVirtualHostNameToFolderMapping("jarvis-orb.app", webRoot,
                CoreWebView2HostResourceAccessKind.Allow);

            _core.Settings.AreDevToolsEnabled = false;
            _core.Settings.AreDefaultContextMenusEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.IsZoomControlEnabled = false;
            _core.Settings.UserAgent = "Jarvis-Orb/1.0";

            _core.WebMessageReceived += OnWebMessageReceived;

            var cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            _core.Navigate($"https://jarvis-orb.app/orb.html?v={cacheBuster}");

            _initialized = true;

            if (_summonPending)
            {
                _summonPending = false;
                Summon();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[NativeOrb] WebView2 init failed: {ex.Message}");
        }
    }

    private static string ExtractOrbAssets()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web");
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var action = doc.RootElement.TryGetProperty("action", out var a) ? a.GetString() : null;

            switch (action)
            {
                case "orb.ready":
                    return;

                case "orb.expand":
                    Expand();
                    return;

                case "orb.collapse":
                    Collapse();
                    return;

                case "orb.dismiss":
                    Dismiss();
                    return;

                case "orb.openFull":
                    OpenOverlayRequested?.Invoke();
                    Collapse();
                    return;

                case "orb.dragStart":
                    _isDragging = true;
                    return;

                case "orb.dragEnd":
                    _isDragging = false;
                    // Save the new position after dragging
                    SavePosition(_currentX, _currentY);
                    return;

                case "orb.savePosition":
                    if (doc.RootElement.TryGetProperty("x", out var xEl) &&
                        doc.RootElement.TryGetProperty("y", out var yEl))
                    {
                        _currentX = xEl.GetInt32();
                        _currentY = yEl.GetInt32();
                        SavePosition(_currentX, _currentY);
                    }
                    return;

                case "voice.start":
                    VoiceModeChanged?.Invoke(true);
                    return;

                case "voice.stop":
                    VoiceModeChanged?.Invoke(false);
                    return;
            }

            await _bridge.HandleMessageAsync(json);
        }
        catch (Exception ex)
        {
            _bridge.PostToWeb(new { @event = "error", message = ex.Message });
        }
    }

    // ── Expand / Collapse ─────────────────────────────────────

    /// <summary>
    /// Expand from compact orb to fullscreen Jarvis chatbot.
    /// The window covers the entire screen — no borders, no chrome.
    /// </summary>
    private void Expand()
    {
        if (_isExpanded || _hwnd == IntPtr.Zero) return;
        _isExpanded = true;

        // Fullscreen — cover the entire primary screen
        var screen = System.Windows.SystemParameters.WorkArea;
        int fw = (int)screen.Width;
        int fh = (int)screen.Height;

        SetWindowPos(_hwnd, (IntPtr)HWND_TOPMOST, 0, 0, fw, fh,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);

        PostMessage(System.Text.Json.JsonSerializer.Serialize(new { @event = "expanded" }));
    }

    /// <summary>
    /// Collapse back to just the floating orb at its saved position.
    /// </summary>
    private void Collapse()
    {
        if (!_isExpanded || _hwnd == IntPtr.Zero) return;
        _isExpanded = false;

        SetWindowPos(_hwnd, (IntPtr)HWND_TOPMOST, _currentX, _currentY,
            CompactW, CompactH, SWP_NOACTIVATE | SWP_SHOWWINDOW);

        PostMessage(System.Text.Json.JsonSerializer.Serialize(new { @event = "collapsed" }));
    }

    // ── Summon / Dismiss ──────────────────────────────────────

    public void Summon()
    {
        if (!_initialized)
        {
            _summonPending = true;
            return;
        }

        if (_isExpanded)
        {
            // Already in fullscreen — just make sure it's visible
            var screen = System.Windows.SystemParameters.WorkArea;
            SetWindowPos(_hwnd, (IntPtr)HWND_TOPMOST, 0, 0,
                (int)screen.Width, (int)screen.Height,
                SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        else
        {
            // Compact orb at saved position
            SetWindowPos(_hwnd, (IntPtr)HWND_TOPMOST, _currentX, _currentY,
                CompactW, CompactH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        PostMessage(System.Text.Json.JsonSerializer.Serialize(new { @event = "summon" }));
    }

    public void Dismiss()
    {
        if (!_initialized || _hwnd == IntPtr.Zero) return;

        PostMessage(System.Text.Json.JsonSerializer.Serialize(new { @event = "dismiss" }));

        var timer = new System.Threading.Timer(_ =>
        {
            ShowWindow(_hwnd, SW_HIDE);
            Dismissed?.Invoke();
        }, null, 350, Timeout.Infinite);
    }

    public void SetState(string state)
    {
        PostMessage(System.Text.Json.JsonSerializer.Serialize(new { @event = "state", state }));
    }

    // ── IBridgeHost ───────────────────────────────────────────

    public void PostMessage(string json) => _core?.PostWebMessageAsJson(json);
    public void NavigateReload() => _core?.Reload();
    public void ToggleDevTools() => _core?.OpenDevToolsWindow();
    public void SetZoom(double z) { if (_controller != null) _controller.ZoomFactor = z; }
    public void BringToFront()
    {
        SetWindowPos(_hwnd, (IntPtr)HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOMOVE | SWP_NOSIZE);
    }
    public void CloseApp() => DestroyWindow(_hwnd);

    // ── Dispose ───────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SavePosition(_currentX, _currentY);

        _controller?.Close();
        _controller = null;
        _core = null;

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        UnregisterClass(ClassName, _hinstance);
    }
}
