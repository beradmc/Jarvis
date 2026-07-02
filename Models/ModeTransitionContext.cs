using System;

namespace JarvisCSharp.Models
{
    /// <summary>
    /// Context information for mode transition event logging and diagnostics.
    /// Captures details about state changes between PASSIVE, ACTIVE, and MUTED modes.
    /// Used for performance analysis, debugging, and audit trails.
    /// </summary>
    public class ModeTransitionContext
    {
        /// <summary>
        /// The mode the system is transitioning from.
        /// </summary>
        public JarvisMode FromMode { get; set; }

        /// <summary>
        /// The mode the system is transitioning to.
        /// </summary>
        public JarvisMode ToMode { get; set; }

        /// <summary>
        /// Human-readable reason for the mode transition.
        /// Examples: "Wake word detected", "Sleep command", "Session error", etc.
        /// </summary>
        public string Reason { get; set; } = "";

        /// <summary>
        /// Timestamp when the mode transition was initiated.
        /// Used for performance analysis and audit logging.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Duration of the mode transition operation.
        /// Used to validate performance requirements (Requirement 13.3: SHALL complete in under 300ms).
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Indicates whether the Gemini session state changed during this transition.
        /// True if session was opened or closed, false if session state remained unchanged.
        /// </summary>
        public bool SessionStateChanged { get; set; }

        /// <summary>
        /// Indicates whether the TTS (Text-to-Speech) state changed during this transition.
        /// True if TTS was enabled or disabled, false if TTS state remained unchanged.
        /// </summary>
        public bool TTSStateChanged { get; set; }

        /// <summary>
        /// Creates a new ModeTransitionContext instance with default values.
        /// </summary>
        public ModeTransitionContext()
        {
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a formatted log message for this mode transition.
        /// Format: "Mode change: [FromMode] → [ToMode] ([Reason]) - Duration: [Duration]ms"
        /// </summary>
        /// <returns>Formatted log message string.</returns>
        public string ToLogMessage()
        {
            var durationMs = Duration.TotalMilliseconds;
            var sessionInfo = SessionStateChanged ? " [Session changed]" : "";
            var ttsInfo = TTSStateChanged ? " [TTS changed]" : "";
            
            return $"Mode change: {FromMode} → {ToMode} ({Reason}) - Duration: {durationMs:F1}ms{sessionInfo}{ttsInfo}";
        }
    }

    /// <summary>
    /// Jarvis operating modes for power management and user interaction control.
    /// Note: This enum should already exist in MainWindow.xaml.cs - this is a reference copy.
    /// </summary>
    public enum JarvisMode
    {
        /// <summary>
        /// PASSIVE: Sleeping/Standby mode. Only wake-word/clap detection active.
        /// Gemini session closed. User must say "Jarvis" or clap to activate.
        /// Hologram: Blue/Calm
        /// </summary>
        PASSIVE,

        /// <summary>
        /// ACTIVE: Fully active and listening mode. Gemini Live Session open.
        /// Continuously listening, responding, executing tools.
        /// User can say "Jarvis kapan" or "Jarvis sleep" to enter PASSIVE mode.
        /// Hologram: Green/Active
        /// </summary>
        ACTIVE,

        /// <summary>
        /// MUTED: Listening but not speaking mode. Gemini session open.
        /// Hears commands, executes tools, but TTS is disabled.
        /// User can say "Jarvis konuş" to return to ACTIVE mode.
        /// Hologram: Orange/Quiet
        /// </summary>
        MUTED
    }
}
