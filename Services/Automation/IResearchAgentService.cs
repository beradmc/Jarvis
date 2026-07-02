namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Service for searching automation solutions online when encountering unknown applications.
    /// Manages web research, GitHub searches, documentation lookup, and pattern suggestions.
    /// </summary>
    public interface IResearchAgentService
    {
        /// <summary>
        /// Search for automation patterns for an application.
        /// </summary>
        /// <param name="applicationName">Application name to research</param>
        /// <returns>Research result with patterns and links</returns>
        Task<ResearchResult> ResearchApplicationAsync(string applicationName);

        /// <summary>
        /// Search GitHub for existing automation scripts.
        /// </summary>
        /// <param name="query">Search query</param>
        /// <returns>List of GitHub results</returns>
        Task<List<GitHubResult>> SearchGitHubAsync(string query);

        /// <summary>
        /// Search Microsoft documentation for UI Automation patterns.
        /// </summary>
        /// <param name="appFramework">Application framework (WPF, WinForms, etc.)</param>
        /// <returns>List of documentation links</returns>
        Task<List<DocumentationResult>> SearchDocumentationAsync(string appFramework);

        /// <summary>
        /// Detect application framework (WPF, WinForms, Electron, Qt, etc.).
        /// </summary>
        /// <param name="windowHandle">Window handle to analyze</param>
        /// <returns>Detected application framework</returns>
        ApplicationFramework DetectFramework(IntPtr windowHandle);

        /// <summary>
        /// Suggest automation patterns based on research.
        /// </summary>
        /// <param name="research">Research result</param>
        /// <returns>List of suggested patterns</returns>
        List<string> SuggestPatterns(ResearchResult research);

        /// <summary>
        /// Get cached research results (7-day cache).
        /// </summary>
        /// <param name="applicationName">Application name</param>
        /// <returns>Cached research result or null if not found/expired</returns>
        ResearchResult? GetCachedResearch(string applicationName);

        /// <summary>
        /// Store application-specific patterns.
        /// </summary>
        /// <param name="applicationName">Application name</param>
        /// <param name="pattern">Automation pattern to store</param>
        void StoreApplicationPattern(string applicationName, AutomationPattern pattern);
    }

    public class ResearchResult
    {
        public string ApplicationName { get; set; } = string.Empty;
        public ApplicationFramework Framework { get; set; }
        public List<AutomationPattern> Patterns { get; set; } = new();
        public List<GitHubResult> GitHubResults { get; set; } = new();
        public List<DocumentationResult> DocumentationLinks { get; set; } = new();
        public DateTime ResearchTime { get; set; }
        public bool IsCached { get; set; }
    }

    public enum ApplicationFramework
    {
        Unknown,
        WPF,
        WinForms,
        Electron,
        Qt,
        Win32,
        UWP,
        Web
    }

    public class AutomationPattern
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public List<string> Examples { get; set; } = new();
        public string Source { get; set; } = string.Empty;
    }

    public class GitHubResult
    {
        public string Repository { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Stars { get; set; }
        public string Language { get; set; } = string.Empty;
    }

    public class DocumentationResult
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }
}
