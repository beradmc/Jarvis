namespace JarvisCSharp.Models
{
    /// <summary>
    /// Result of transcription command detection analysis.
    /// Used by the Mode Manager to determine what command (if any) was detected in the transcription text.
    /// </summary>
    public enum CommandDetectionResult
    {
        /// <summary>
        /// No recognized command in transcription text.
        /// </summary>
        NO_COMMAND,

        /// <summary>
        /// Wake word detected in transcription (e.g., "jarvis", "hey jarvis").
        /// Should trigger transition from MUTED to ACTIVE mode.
        /// </summary>
        WAKE_WORD,

        /// <summary>
        /// Sleep/passive command detected (e.g., "jarvis kapat", "jarvis sleep").
        /// Should trigger transition to MUTED or PASSIVE mode.
        /// </summary>
        SLEEP_COMMAND,

        /// <summary>
        /// Mute command detected (e.g., "jarvis sus").
        /// Should trigger transition to MUTED mode.
        /// </summary>
        MUTE_COMMAND,

        /// <summary>
        /// Unmute command detected (e.g., "jarvis konuş", "jarvis speak").
        /// Should trigger transition from MUTED to ACTIVE mode.
        /// </summary>
        UNMUTE_COMMAND
    }
}
