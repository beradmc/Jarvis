using System;

namespace JarvisCSharp.Config
{
    /// <summary>
    /// Configuration constants for intelligent listening mode behavior.
    /// Contains wake word variations, sleep commands, hologram colors, and performance timing constants.
    /// </summary>
    public static class ModeConfiguration
    {
        // ── Wake Word Cooldown (Requirement 10.1) ─────────────────────────────

        /// <summary>
        /// Wake word detection cooldown duration after transitioning from MUTED to ACTIVE mode.
        /// Prevents false positives when user mentions "Jarvis" in conversation.
        /// </summary>
        public const int WAKE_WORD_COOLDOWN_SECONDS = 5;

        // ── Performance Timing Constraints (Requirement 13) ───────────────────

        /// <summary>
        /// Maximum time for hardware wake word detection (WakeupListener) to trigger.
        /// Requirement 13.1: SHALL trigger within 500 milliseconds.
        /// </summary>
        public const int HARDWARE_WAKE_DETECTION_MAX_MS = 500;

        /// <summary>
        /// Maximum time for transcription-based wake word detection to trigger.
        /// Requirement 13.2: SHALL trigger within 300 milliseconds.
        /// </summary>
        public const int TRANSCRIPTION_WAKE_DETECTION_MAX_MS = 300;

        /// <summary>
        /// Maximum time for mode transition from MUTED to ACTIVE mode.
        /// Requirement 13.3: SHALL complete in under 300 milliseconds.
        /// </summary>
        public const int MODE_TRANSITION_MAX_MS = 300;

        /// <summary>
        /// Maximum time for TTS service enable/disable operation.
        /// Requirement 13.4: SHALL complete in under 50 milliseconds.
        /// </summary>
        public const int TTS_TOGGLE_MAX_MS = 50;

        /// <summary>
        /// Maximum time for hologram color change (1 frame at 60fps).
        /// Requirement 13.5: SHALL be visible within 1 frame (16ms at 60fps).
        /// </summary>
        public const int HOLOGRAM_UPDATE_MAX_MS = 16;

        /// <summary>
        /// Maximum time for status text update in UI.
        /// Requirement 13.6: SHALL be visible within 100 milliseconds.
        /// </summary>
        public const int STATUS_UPDATE_MAX_MS = 100;

        /// <summary>
        /// Maximum time for Gemini session initialization when entering ACTIVE mode.
        /// Requirement 13.7: SHALL complete within 2 seconds.
        /// </summary>
        public const int SESSION_INIT_MAX_MS = 2000;

        // ── VAD Timing (Requirement 18) ───────────────────────────────────────

        /// <summary>
        /// Voice Activity Detection silence threshold duration.
        /// Requirement 18.1: SHALL be 1500 milliseconds in both ACTIVE and MUTED modes.
        /// </summary>
        public const int VAD_SILENCE_THRESHOLD_MS = 1500;

        // ── Wake Word Variations (Requirement 1.6) ────────────────────────────

        /// <summary>
        /// Supported wake word variations for activating Jarvis.
        /// Includes English and Turkish variations.
        /// Requirement 1.6: SHALL support multiple wake word variations.
        /// </summary>
        public static readonly string[] WAKE_WORDS = new[]
        {
            "jarvis",
            "hey jarvis",
            "ok jarvis",
            "jarvis konuş",      // Turkish: "Jarvis speak"
            "jarvis speak",
            "jarvis açıl"        // Turkish: "Jarvis open up"
        };

        // ── Sleep Command Variations (Requirement 2.4) ────────────────────────

        /// <summary>
        /// Supported sleep command variations for entering MUTED mode.
        /// Includes English and Turkish variations.
        /// Requirement 2.4: SHALL recognize multiple sleep command variations.
        /// </summary>
        public static readonly string[] SLEEP_COMMANDS = new[]
        {
            "jarvis kapat",          // Turkish: "Jarvis close" → full shutdown (PASSIVE)
            "jarvis uyku",           // Turkish: "Jarvis sleep"
            "jarvis sleep",
            "jarvis uyku modu",      // Turkish: "Jarvis sleep mode"
            "jarvis go to sleep"
        };

        // ── Mute Command Variations ───────────────────────────────────────────

        /// <summary>
        /// Supported mute command variations for entering MUTED mode.
        /// MUTED mode: Jarvis keeps listening but does not respond.
        /// Session stays open, TTS is disabled. Say "Jarvis" to unmute.
        /// </summary>
        public static readonly string[] MUTE_COMMANDS = new[]
        {
            "jarvis kapan",          // Turkish: "Jarvis close" (imperative) → mute
            "jarvis sus",            // Turkish: "Jarvis be quiet" → mute
            "jarvis sessiz",         // Turkish: "Jarvis silent" → mute
            "jarvis sessiz ol",      // Turkish: "Jarvis be silent" → mute
            "jarvis konuşma",        // Turkish: "Jarvis don't speak" → mute
            "jarvis mute"            // English: "Jarvis mute" → mute
        };

        // ── Hologram Colors (Requirement 6.1-6.3, 6.7) ────────────────────────

        /// <summary>
        /// Hologram color for PASSIVE mode (sleeping/standby).
        /// Requirement 6.1: Blue color (RGB: 0, 100, 255).
        /// </summary>
        public static readonly (byte R, byte G, byte B) COLOR_PASSIVE = (0, 100, 255);

        /// <summary>
        /// Hologram color for ACTIVE mode (fully active and listening).
        /// Requirement 6.2: Green color (RGB: 0, 200, 180).
        /// </summary>
        public static readonly (byte R, byte G, byte B) COLOR_ACTIVE = (0, 200, 180);

        /// <summary>
        /// Hologram color for MUTED mode (listening but not speaking).
        /// Requirement 6.3: Orange color (RGB: 255, 150, 0).
        /// </summary>
        public static readonly (byte R, byte G, byte B) COLOR_MUTED = (255, 150, 0);

        /// <summary>
        /// Hologram color when Jarvis is speaking in any mode.
        /// Requirement 6.7: Orange/white color (RGB: 80, 200, 255) with increased rotation speed.
        /// </summary>
        public static readonly (byte R, byte G, byte B) COLOR_SPEAKING = (80, 200, 255);
    }
}
