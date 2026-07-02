using System.Drawing;

namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Service for analyzing screenshots to identify UI elements using Gemini vision AI.
    /// Responsible for screenshot capture, element detection, coordinate transformation, and OCR.
    /// </summary>
    public interface IVisionEngineService
    {
        /// <summary>
        /// Capture and analyze a specific target (window, monitor, or all monitors).
        /// </summary>
        /// <param name="query">User's description of what to find</param>
        /// <param name="targetSpec">Target specification: "active_window", "cursor_monitor", "all_monitors", etc.</param>
        /// <param name="options">Optional vision analysis options</param>
        /// <returns>Vision analysis result with detected elements and coordinates</returns>
        Task<VisionAnalysisResult> AnalyzeTargetAsync(
            string query,
            string targetSpec,
            VisionOptions? options = null
        );

        /// <summary>
        /// Extract text from a screen region using OCR.
        /// </summary>
        /// <param name="region">Screen region to extract text from</param>
        /// <param name="language">Language code ("tr" or "en")</param>
        /// <returns>OCR result with extracted text</returns>
        Task<OcrResult> ExtractTextAsync(
            Rectangle region,
            string language = "tr"
        );

        /// <summary>
        /// Transform relative coordinates to absolute screen coordinates.
        /// </summary>
        /// <param name="relativeCoord">Relative coordinate within the captured region</param>
        /// <param name="context">Capture context with offset information</param>
        /// <returns>Absolute screen coordinate</returns>
        Point TransformToAbsolute(Point relativeCoord, CaptureContext context);

        /// <summary>
        /// Get cached analysis if available (within configured cache duration).
        /// </summary>
        /// <param name="targetSpec">Target specification to lookup in cache</param>
        /// <returns>Cached analysis result or null if not found/expired</returns>
        VisionAnalysisResult? GetCachedAnalysis(string targetSpec);
    }

    public class VisionAnalysisResult
    {
        public List<DetectedElement> Elements { get; set; } = new();
        public string AnalysisText { get; set; } = string.Empty;
        public CaptureContext Context { get; set; } = new();
        public DateTime Timestamp { get; set; }
    }

    public class DetectedElement
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Point AbsoluteCoordinate { get; set; }
        public int ConfidenceScore { get; set; }
        public Rectangle Bounds { get; set; }
    }

    public class CaptureContext
    {
        public string TargetTitle { get; set; } = string.Empty;
        public Point Offset { get; set; }
        public Size CaptureSize { get; set; }
        public string TargetType { get; set; } = string.Empty;
        public IntPtr WindowHandle { get; set; }
    }

    public class VisionOptions
    {
        public int MinConfidenceScore { get; set; } = 70;
        public bool UseCache { get; set; } = true;
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromSeconds(2);
        public string[] PreferredModels { get; set; } = { "gemini-2.5-flash", "gemini-2.0-flash" };
    }

    public class OcrResult
    {
        public string Text { get; set; } = string.Empty;
        public int ConfidenceScore { get; set; }
        public Rectangle Region { get; set; }
        public string Language { get; set; } = string.Empty;
        public List<OcrLine> Lines { get; set; } = new();
        
        /// <summary>
        /// True when confidence score is below 80%, indicating low confidence extraction
        /// </summary>
        public bool HasLowConfidence => ConfidenceScore < 80;
        
        /// <summary>
        /// Warning message when confidence is low (below 80%)
        /// </summary>
        public string? ConfidenceWarning => HasLowConfidence 
            ? $"Low confidence ({ConfidenceScore}%). Text extraction may be inaccurate." 
            : null;
        
        /// <summary>
        /// Error message if OCR extraction failed
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    public class OcrLine
    {
        public string Text { get; set; } = string.Empty;
        public Rectangle Bounds { get; set; }
        public int ConfidenceScore { get; set; }
    }
}
