using System.Drawing;

namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Service for executing UI interactions (clicks, typing, shortcuts, window management).
    /// Responsible for input simulation and window control operations.
    /// </summary>
    public interface IAutomationControllerService
    {
        /// <summary>
        /// Click at absolute screen coordinates.
        /// </summary>
        /// <param name="screenCoordinate">Absolute screen coordinate to click</param>
        /// <param name="type">Type of click (left, right, double, middle)</param>
        /// <param name="ensureFocus">Whether to ensure target window has focus before clicking</param>
        /// <returns>Execution result</returns>
        Task<ExecutionResult> ClickAsync(
            Point screenCoordinate,
            ClickType type = ClickType.LeftSingle,
            bool ensureFocus = true
        );

        /// <summary>
        /// Type text into the focused element or at a coordinate.
        /// </summary>
        /// <param name="text">Text to type</param>
        /// <param name="coordinate">Optional coordinate to click before typing</param>
        /// <param name="clearExisting">Whether to clear existing text first</param>
        /// <returns>Execution result</returns>
        Task<ExecutionResult> TypeTextAsync(
            string text,
            Point? coordinate = null,
            bool clearExisting = true
        );

        /// <summary>
        /// Send keyboard shortcut to active window.
        /// </summary>
        /// <param name="shortcut">Shortcut string (e.g., "Ctrl+S", "Alt+F4")</param>
        /// <param name="targetWindow">Optional target window handle</param>
        /// <returns>Execution result</returns>
        Task<ExecutionResult> SendShortcutAsync(
            string shortcut,
            IntPtr? targetWindow = null
        );

        /// <summary>
        /// Send key sequence (e.g., "Alt+F, S" for File > Save).
        /// </summary>
        /// <param name="keys">Array of key combinations</param>
        /// <param name="delayMs">Delay between keys in milliseconds</param>
        /// <returns>Execution result</returns>
        Task<ExecutionResult> SendKeySequenceAsync(
            string[] keys,
            int delayMs = 100
        );

        /// <summary>
        /// Launch application by name or path.
        /// </summary>
        /// <param name="appNameOrPath">Application name or full path to executable</param>
        /// <param name="timeoutSeconds">Maximum time to wait for app window to appear</param>
        /// <returns>Execution result</returns>
        Task<ExecutionResult> LaunchApplicationAsync(
            string appNameOrPath,
            int timeoutSeconds = 10
        );

        /// <summary>
        /// Bring window to focus.
        /// </summary>
        Task<ExecutionResult> BringWindowToFocusAsync(IntPtr windowHandle);

        /// <summary>
        /// Maximize window.
        /// </summary>
        Task<ExecutionResult> MaximizeWindowAsync(IntPtr windowHandle);

        /// <summary>
        /// Minimize window.
        /// </summary>
        Task<ExecutionResult> MinimizeWindowAsync(IntPtr windowHandle);

        /// <summary>
        /// Close window.
        /// </summary>
        /// <param name="windowHandle">Window to close</param>
        /// <param name="force">Whether to force close without save prompts</param>
        /// <returns>Execution result</returns>
        Task<ExecutionResult> CloseWindowAsync(IntPtr windowHandle, bool force = false);

        /// <summary>
        /// Move/position window.
        /// </summary>
        Task<ExecutionResult> MoveWindowAsync(IntPtr windowHandle, WindowPosition position);

        /// <summary>
        /// Drag from start to end coordinate.
        /// </summary>
        /// <param name="start">Start coordinate</param>
        /// <param name="end">End coordinate</param>
        /// <param name="durationMs">Duration of drag operation</param>
        /// <returns>Execution result</returns>
        Task<ExecutionResult> DragAsync(Point start, Point end, int durationMs = 500);

        /// <summary>
        /// Get all monitors and their configuration.
        /// </summary>
        List<MonitorInfo> GetMonitorConfiguration();

        /// <summary>
        /// Apply DPI scaling to coordinate for a specific monitor.
        /// </summary>
        Point ApplyDpiScaling(Point coordinate, MonitorInfo monitor);
    }

    public enum ClickType
    {
        LeftSingle,
        LeftDouble,
        RightSingle,
        MiddleSingle
    }

    public enum WindowPosition
    {
        Maximize,
        Minimize,
        LeftHalf,
        RightHalf,
        TopHalf,
        BottomHalf,
        Center,
        Monitor1,
        Monitor2
    }

    public class ExecutionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Error { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class MonitorInfo
    {
        public int Index { get; set; }
        public Rectangle Bounds { get; set; }
        public Rectangle WorkingArea { get; set; }
        public bool IsPrimary { get; set; }
        public float DpiScaleFactor { get; set; }
    }
}
