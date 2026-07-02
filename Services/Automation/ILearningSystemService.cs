using System.Drawing;

namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Service for storing user corrections and improving element recognition over time.
    /// Manages learning database, corrections, aliases, and usage statistics.
    /// </summary>
    public interface ILearningSystemService
    {
        /// <summary>
        /// Store a correction for misidentified element.
        /// </summary>
        /// <param name="applicationName">Application name</param>
        /// <param name="elementDescription">Element description that was incorrect</param>
        /// <param name="correctElement">Correct element information</param>
        /// <param name="screenshotHash">Screenshot hash for visual similarity</param>
        void StoreCorrection(
            string applicationName,
            string elementDescription,
            DetectedElement correctElement,
            byte[] screenshotHash
        );

        /// <summary>
        /// Retrieve corrections for an application.
        /// </summary>
        /// <param name="applicationName">Application name</param>
        /// <returns>List of corrections</returns>
        List<ElementCorrection> GetCorrections(string applicationName);

        /// <summary>
        /// Boost confidence score based on past corrections.
        /// </summary>
        /// <param name="applicationName">Application name</param>
        /// <param name="elementDescription">Element description</param>
        /// <param name="originalScore">Original confidence score</param>
        /// <returns>Boosted confidence score</returns>
        int BoostConfidenceScore(
            string applicationName,
            string elementDescription,
            int originalScore
        );

        /// <summary>
        /// Check if element has user-defined alias.
        /// </summary>
        /// <param name="elementDescription">Element description or alias</param>
        /// <param name="applicationName">Application name</param>
        /// <returns>Canonical name or null if no alias found</returns>
        string? ResolveAlias(string elementDescription, string applicationName);

        /// <summary>
        /// Add user-defined alias.
        /// </summary>
        /// <param name="alias">Alias to add</param>
        /// <param name="canonicalName">Canonical element name</param>
        /// <param name="applicationName">Application name</param>
        void AddAlias(string alias, string canonicalName, string applicationName);

        /// <summary>
        /// Check if should suggest creating custom automation script.
        /// </summary>
        /// <param name="applicationName">Application name</param>
        /// <returns>True if should suggest custom script</returns>
        bool ShouldSuggestCustomScript(string applicationName);

        /// <summary>
        /// Get usage statistics for corrections.
        /// </summary>
        /// <param name="applicationName">Application name</param>
        /// <returns>Dictionary of element descriptions to usage counts</returns>
        Dictionary<string, int> GetCorrectionStats(string applicationName);

        /// <summary>
        /// Apply learning immediately without restart.
        /// </summary>
        void RefreshLearningModel();
    }

    public class ElementCorrection
    {
        public string ApplicationName { get; set; } = string.Empty;
        public string ElementDescription { get; set; } = string.Empty;
        public Point CorrectCoordinate { get; set; }
        public Rectangle CorrectBounds { get; set; }
        public byte[] ScreenshotHash { get; set; } = Array.Empty<byte>();
        public DateTime CorrectionTime { get; set; }
        public int UsageCount { get; set; }
        public List<string> VisualCharacteristics { get; set; } = new();
    }

    public class LearningDatabase
    {
        public Dictionary<string, List<ElementCorrection>> Corrections { get; set; } = new();
        public Dictionary<string, Dictionary<string, string>> Aliases { get; set; } = new();
        public DateTime LastUpdated { get; set; }
    }
}
