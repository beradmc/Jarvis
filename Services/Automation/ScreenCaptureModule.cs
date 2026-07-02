using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Screen capture module with multi-monitor support and coordinate context tracking.
    /// Captures windows, monitors, or the entire virtual desktop while maintaining coordinate transformation context.
    /// </summary>
    public class ScreenCaptureModule
    {
        #region Win32 API Imports

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, DpiType dpiType, out uint dpiX, out uint dpiY);

        private enum DpiType
        {
            Effective = 0,
            Angular = 1,
            Raw = 2,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        // System metrics constants
        private const int SM_XVIRTUALSCREEN = 76;  // Virtual screen left
        private const int SM_YVIRTUALSCREEN = 77;  // Virtual screen top
        private const int SM_CXVIRTUALSCREEN = 78; // Virtual screen width
        private const int SM_CYVIRTUALSCREEN = 79; // Virtual screen height

        #endregion

        #region Public Models

        /// <summary>
        /// Context information about a captured screen region, used for coordinate transformations.
        /// </summary>
        public class CaptureContext
        {
            /// <summary>Title of the captured target (window title or monitor description)</summary>
            public string TargetTitle { get; set; } = "";

            /// <summary>Top-left corner offset in virtual desktop coordinates</summary>
            public Point Offset { get; set; }

            /// <summary>Size of the captured region</summary>
            public Size CaptureSize { get; set; }

            /// <summary>Type of target: "window", "monitor", or "virtual_desktop"</summary>
            public string TargetType { get; set; } = "window";

            /// <summary>Window handle if target is a window, otherwise IntPtr.Zero</summary>
            public IntPtr WindowHandle { get; set; }

            /// <summary>Monitor index if target is a monitor, otherwise -1</summary>
            public int MonitorIndex { get; set; } = -1;
        }

        /// <summary>
        /// Result of a screen capture operation.
        /// </summary>
        public class CaptureResult
        {
            /// <summary>Captured bitmap image</summary>
            public Bitmap? Image { get; set; }

            /// <summary>Capture context for coordinate transformations</summary>
            public CaptureContext Context { get; set; } = new CaptureContext();

            /// <summary>Whether the capture was successful</summary>
            public bool Success { get; set; }

            /// <summary>Error message if capture failed</summary>
            public string? ErrorMessage { get; set; }
        }

        /// <summary>
        /// Information about a monitor in the system.
        /// </summary>
        public class MonitorInfo
        {
            /// <summary>Monitor index (0-based)</summary>
            public int Index { get; set; }

            /// <summary>Monitor bounds in virtual desktop coordinates</summary>
            public Rectangle Bounds { get; set; }

            /// <summary>Working area (bounds minus taskbar)</summary>
            public Rectangle WorkingArea { get; set; }

            /// <summary>Whether this is the primary monitor</summary>
            public bool IsPrimary { get; set; }

            /// <summary>DPI scale factor (1.0 = 96 DPI, 1.25 = 120 DPI, etc.)</summary>
            public float DpiScaleFactor { get; set; } = 1.0f;

            /// <summary>Device name</summary>
            public string DeviceName { get; set; } = "";
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets configuration of all monitors in the system.
        /// </summary>
        /// <returns>List of monitor information</returns>
        public List<MonitorInfo> GetMonitorConfiguration()
        {
            var monitors = new List<MonitorInfo>();
            var screens = Screen.AllScreens;

            for (int i = 0; i < screens.Length; i++)
            {
                var screen = screens[i];
                var info = new MonitorInfo
                {
                    Index = i,
                    Bounds = screen.Bounds,
                    WorkingArea = screen.WorkingArea,
                    IsPrimary = screen.Primary,
                    DeviceName = screen.DeviceName
                };

                // Get DPI scaling for this monitor
                try
                {
                    IntPtr hMonitor = GetMonitorHandle(screen);
                    if (hMonitor != IntPtr.Zero)
                    {
                        if (GetDpiForMonitor(hMonitor, DpiType.Effective, out uint dpiX, out uint dpiY) == 0)
                        {
                            info.DpiScaleFactor = dpiX / 96.0f; // 96 DPI is 100% scaling
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to get DPI for monitor {i}: {ex.Message}");
                }

                monitors.Add(info);
            }

            Logger.Information($"[ScreenCapture] Detected {monitors.Count} monitors");
            return monitors;
        }

        /// <summary>
        /// Captures a specific window with coordinate context.
        /// </summary>
        /// <param name="windowHandle">Handle to the window to capture</param>
        /// <returns>Capture result with image and context</returns>
        public CaptureResult CaptureWindow(IntPtr windowHandle)
        {
            var result = new CaptureResult();

            try
            {
                if (windowHandle == IntPtr.Zero)
                {
                    result.ErrorMessage = "Invalid window handle";
                    return result;
                }

                // Get window client rectangle
                if (!GetClientRect(windowHandle, out RECT clientRect))
                {
                    result.ErrorMessage = "Failed to get window client rectangle";
                    return result;
                }

                // Convert client area top-left to screen coordinates
                POINT topLeft = new POINT { X = 0, Y = 0 };
                if (!ClientToScreen(windowHandle, ref topLeft))
                {
                    result.ErrorMessage = "Failed to convert client coordinates to screen";
                    return result;
                }

                int width = clientRect.Right - clientRect.Left;
                int height = clientRect.Bottom - clientRect.Top;

                // Validate dimensions
                if (width <= 0 || height <= 0)
                {
                    result.ErrorMessage = $"Invalid window dimensions: {width}x{height}";
                    return result;
                }

                // Capture the window region
                var captureRect = new Rectangle(topLeft.X, topLeft.Y, width, height);
                var bitmap = CaptureScreenRegion(captureRect);

                if (bitmap == null)
                {
                    result.ErrorMessage = "Failed to capture screen region";
                    return result;
                }

                // Build context
                result.Context = new CaptureContext
                {
                    TargetTitle = GetWindowTitle(windowHandle),
                    Offset = new Point(topLeft.X, topLeft.Y),
                    CaptureSize = new Size(width, height),
                    TargetType = "window",
                    WindowHandle = windowHandle
                };

                result.Image = bitmap;
                result.Success = true;

                Logger.Information($"[ScreenCapture] Captured window '{result.Context.TargetTitle}' " +
                                 $"at ({topLeft.X}, {topLeft.Y}) size {width}x{height}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to capture window");
                result.ErrorMessage = $"Exception: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Captures a full monitor by index.
        /// </summary>
        /// <param name="monitorIndex">Monitor index (0-based)</param>
        /// <returns>Capture result with image and context</returns>
        public CaptureResult CaptureMonitor(int monitorIndex)
        {
            var result = new CaptureResult();

            try
            {
                var screens = Screen.AllScreens;

                if (monitorIndex < 0 || monitorIndex >= screens.Length)
                {
                    result.ErrorMessage = $"Invalid monitor index {monitorIndex}. Valid range: 0-{screens.Length - 1}";
                    return result;
                }

                var screen = screens[monitorIndex];
                var bounds = screen.Bounds;

                // Capture the monitor region
                var bitmap = CaptureScreenRegion(bounds);

                if (bitmap == null)
                {
                    result.ErrorMessage = "Failed to capture monitor region";
                    return result;
                }

                // Build context
                var monitorName = screen.Primary ? "Primary Monitor" : $"Monitor {monitorIndex + 1}";
                result.Context = new CaptureContext
                {
                    TargetTitle = monitorName,
                    Offset = new Point(bounds.X, bounds.Y),
                    CaptureSize = new Size(bounds.Width, bounds.Height),
                    TargetType = "monitor",
                    MonitorIndex = monitorIndex
                };

                result.Image = bitmap;
                result.Success = true;

                Logger.Information($"[ScreenCapture] Captured {monitorName} " +
                                 $"at ({bounds.X}, {bounds.Y}) size {bounds.Width}x{bounds.Height}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to capture monitor {monitorIndex}");
                result.ErrorMessage = $"Exception: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Captures all monitors as a single image (entire virtual desktop).
        /// </summary>
        /// <returns>Capture result with image and context</returns>
        public CaptureResult CaptureVirtualDesktop()
        {
            var result = new CaptureResult();

            try
            {
                var virtualBounds = GetVirtualScreenBounds();

                // Capture the entire virtual desktop
                var bitmap = CaptureScreenRegion(virtualBounds);

                if (bitmap == null)
                {
                    result.ErrorMessage = "Failed to capture virtual desktop";
                    return result;
                }

                // Build context
                var monitorCount = Screen.AllScreens.Length;
                result.Context = new CaptureContext
                {
                    TargetTitle = $"Virtual Desktop ({monitorCount} monitors)",
                    Offset = new Point(virtualBounds.X, virtualBounds.Y),
                    CaptureSize = new Size(virtualBounds.Width, virtualBounds.Height),
                    TargetType = "virtual_desktop"
                };

                result.Image = bitmap;
                result.Success = true;

                Logger.Information($"[ScreenCapture] Captured virtual desktop " +
                                 $"at ({virtualBounds.X}, {virtualBounds.Y}) size {virtualBounds.Width}x{virtualBounds.Height}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to capture virtual desktop");
                result.ErrorMessage = $"Exception: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Saves a capture result to a PNG file.
        /// </summary>
        /// <param name="result">Capture result to save</param>
        /// <param name="filePath">Target file path (optional, generates temp path if null)</param>
        /// <returns>Path to saved file, or null on failure</returns>
        public string? SaveCapture(CaptureResult result, string? filePath = null)
        {
            try
            {
                if (!result.Success || result.Image == null)
                {
                    Logger.Error("Cannot save failed capture or null image");
                    return null;
                }

                // Generate temp path if not provided
                if (string.IsNullOrEmpty(filePath))
                {
                    filePath = Path.Combine(Path.GetTempPath(), $"jarvis-capture-{Guid.NewGuid():N}.png");
                }

                result.Image.Save(filePath, ImageFormat.Png);
                Logger.Information($"[ScreenCapture] Saved capture to {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save capture");
                return null;
            }
        }

        /// <summary>
        /// Applies DPI scaling transformation to a logical coordinate.
        /// Transforms logical coordinates to physical screen coordinates accounting for monitor DPI scaling.
        /// </summary>
        /// <param name="coordinate">Logical coordinate point</param>
        /// <param name="monitor">Monitor information containing DPI scale factor</param>
        /// <returns>Physical screen coordinate with DPI scaling applied</returns>
        public Point ApplyDpiScaling(Point coordinate, MonitorInfo monitor)
        {
            if (monitor == null)
            {
                Logger.Warning("[ScreenCapture] Null monitor provided to ApplyDpiScaling, returning unscaled coordinate");
                return coordinate;
            }

            // Apply DPI scale factor to transform logical to physical coordinates
            int scaledX = (int)Math.Round(coordinate.X * monitor.DpiScaleFactor);
            int scaledY = (int)Math.Round(coordinate.Y * monitor.DpiScaleFactor);

            Logger.Debug($"[ScreenCapture] Applied DPI scaling {monitor.DpiScaleFactor}x: ({coordinate.X}, {coordinate.Y}) -> ({scaledX}, {scaledY})");

            return new Point(scaledX, scaledY);
        }

        /// <summary>
        /// Reverses DPI scaling transformation from physical to logical coordinates.
        /// Transforms physical screen coordinates back to logical coordinates.
        /// </summary>
        /// <param name="coordinate">Physical screen coordinate</param>
        /// <param name="monitor">Monitor information containing DPI scale factor</param>
        /// <returns>Logical coordinate with DPI scaling removed</returns>
        public Point RemoveDpiScaling(Point coordinate, MonitorInfo monitor)
        {
            if (monitor == null)
            {
                Logger.Warning("[ScreenCapture] Null monitor provided to RemoveDpiScaling, returning unscaled coordinate");
                return coordinate;
            }

            // Remove DPI scale factor to transform physical to logical coordinates
            int unscaledX = (int)Math.Round(coordinate.X / monitor.DpiScaleFactor);
            int unscaledY = (int)Math.Round(coordinate.Y / monitor.DpiScaleFactor);

            Logger.Debug($"[ScreenCapture] Removed DPI scaling {monitor.DpiScaleFactor}x: ({coordinate.X}, {coordinate.Y}) -> ({unscaledX}, {unscaledY})");

            return new Point(unscaledX, unscaledY);
        }

        #endregion

        #region Private Helper Methods

        /// <summary>
        /// Gets the virtual screen bounds (entire desktop area across all monitors).
        /// </summary>
        private Rectangle GetVirtualScreenBounds()
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);

            // Fallback to primary screen if metrics fail
            if (w <= 0 || h <= 0)
            {
                var primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                return primary.Bounds;
            }

            return new Rectangle(x, y, w, h);
        }

        /// <summary>
        /// Captures a specific screen region.
        /// </summary>
        private Bitmap? CaptureScreenRegion(Rectangle region)
        {
            try
            {
                var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        region.X,
                        region.Y,
                        0,
                        0,
                        new Size(region.Width, region.Height),
                        CopyPixelOperation.SourceCopy
                    );
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to capture region {region}");
                return null;
            }
        }

        /// <summary>
        /// Gets the title of a window.
        /// </summary>
        private string GetWindowTitle(IntPtr windowHandle)
        {
            try
            {
                int length = GetWindowTextLengthW(windowHandle);
                if (length == 0) return "Untitled Window";

                var sb = new StringBuilder(length + 1);
                GetWindowTextW(windowHandle, sb, sb.Capacity);
                return sb.ToString();
            }
            catch
            {
                return "Unknown Window";
            }
        }

        /// <summary>
        /// Gets the monitor handle for a Screen object.
        /// </summary>
        private IntPtr GetMonitorHandle(Screen screen)
        {
            // Use reflection to get hmonitor from Screen
            try
            {
                var hmonitorField = typeof(Screen).GetField("hmonitor", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (hmonitorField != null)
                {
                    return (IntPtr)(hmonitorField.GetValue(screen) ?? IntPtr.Zero);
                }
            }
            catch
            {
                // Fallback: cannot get hmonitor
            }

            return IntPtr.Zero;
        }

        #endregion
    }
}
