using System;
using System.Runtime.InteropServices;
using System.Text;

namespace JarvisCSharp.Services
{
    public static class Win32Interop
    {
        // PInvoke declarations
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;

        /// <summary>
        /// Gets the title of the currently active (foreground) window.
        /// </summary>
        public static string GetActiveWindowTitle()
        {
            const int nChars = 256;
            StringBuilder buff = new StringBuilder(nChars);
            IntPtr handle = GetForegroundWindow();

            if (GetWindowText(handle, buff, nChars) > 0)
            {
                return buff.ToString();
            }
            return null;
        }

        /// <summary>
        /// Brings a window to the foreground by its handle.
        /// </summary>
        public static void BringToFront(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                SetForegroundWindow(handle);
                ShowWindow(handle, SW_SHOWNORMAL);
            }
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static System.Collections.Generic.List<(IntPtr Handle, string Title)> GetAllVisibleWindows()
        {
            var windows = new System.Collections.Generic.List<(IntPtr Handle, string Title)>();
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    const int nChars = 256;
                    StringBuilder buff = new StringBuilder(nChars);
                    if (GetWindowText(hWnd, buff, nChars) > 0)
                    {
                        var title = buff.ToString();
                        if (!string.IsNullOrWhiteSpace(title) && title != "Program Manager")
                        {
                            windows.Add((hWnd, title));
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
            return windows;
        }
    }
}
