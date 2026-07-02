using System.Drawing;

namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Service for tracking window states, positions, and maintaining context awareness.
    /// Manages window information, interaction history, and user preferences.
    /// </summary>
    public interface IContextManagerService
    {
        /// <summary>
        /// Track all visible windows with positions and z-order.
        /// </summary>
        /// <returns>List of all tracked windows</returns>
        List<WindowInfo> GetAllWindows();

        /// <summary>
        /// Resolve "current window" or "this application" reference.
        /// </summary>
        /// <returns>Current window info or null if cannot be resolved</returns>
        WindowInfo? ResolveCurrentWindow();

        /// <summary>
        /// Get window by title query.
        /// </summary>
        /// <param name="titleQuery">Window title or partial title</param>
        /// <returns>Window info or null if not found</returns>
        WindowInfo? FindWindowByTitle(string titleQuery);

        /// <summary>
        /// Get interaction history (last N actions).
        /// </summary>
        /// <param name="count">Number of recent interactions to retrieve</param>
        /// <returns>List of interaction records</returns>
        List<InteractionRecord> GetInteractionHistory(int count = 20);

        /// <summary>
        /// Add interaction to history.
        /// </summary>
        /// <param name="interaction">Interaction record to add</param>
        void RecordInteraction(InteractionRecord interaction);

        /// <summary>
        /// Store window preferences.
        /// </summary>
        /// <param name="appName">Application name</param>
        /// <param name="preference">Window preference to store</param>
        void SaveWindowPreference(string appName, WindowPreference preference);

        /// <summary>
        /// Retrieve window preferences.
        /// </summary>
        /// <param name="appName">Application name</param>
        /// <returns>Window preference or null if not found</returns>
        WindowPreference? GetWindowPreference(string appName);

        /// <summary>
        /// Event fired when window state changes.
        /// </summary>
        event Action<WindowStateChange>? OnWindowStateChanged;

        /// <summary>
        /// Get monitor for a window.
        /// </summary>
        /// <param name="windowHandle">Window handle</param>
        /// <returns>Monitor info</returns>
        MonitorInfo GetMonitorForWindow(IntPtr windowHandle);

        /// <summary>
        /// Disambiguate target using context.
        /// </summary>
        /// <param name="ambiguousRef">Ambiguous reference (e.g., "this window", "that app")</param>
        /// <param name="candidates">List of candidate windows</param>
        /// <returns>Best match window or null if cannot disambiguate</returns>
        WindowInfo? DisambiguateTarget(string ambiguousRef, List<WindowInfo> candidates);
    }

    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public Rectangle Bounds { get; set; }
        public int ZOrder { get; set; }
        public bool IsVisible { get; set; }
        public bool IsForeground { get; set; }
        public MonitorInfo Monitor { get; set; } = new();
        public DateTime LastInteracted { get; set; }
    }

    public class InteractionRecord
    {
        public DateTime Timestamp { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public WindowInfo TargetWindow { get; set; } = new();
        public Dictionary<string, object> Parameters { get; set; } = new();
        public bool Success { get; set; }
    }

    public class WindowPreference
    {
        public string AppName { get; set; } = string.Empty;
        public Rectangle? PreferredBounds { get; set; }
        public WindowPosition? PreferredPosition { get; set; }
        public int? PreferredMonitor { get; set; }
        public DateTime LastUsed { get; set; }
    }

    public class WindowStateChange
    {
        public IntPtr WindowHandle { get; set; }
        public WindowStateChangeType ChangeType { get; set; }
        public object? OldValue { get; set; }
        public object? NewValue { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum WindowStateChangeType
    {
        Created,
        Closed,
        Moved,
        Resized,
        FocusChanged,
        MonitorChanged
    }
}
