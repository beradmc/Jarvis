using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JarvisCSharp.Config;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// Service for analyzing screenshots to identify UI elements using Gemini vision AI.
    /// Implements robust retry logic with exponential backoff and model fallback.
    /// </summary>
    public class VisionEngineService : IVisionEngineService
    {
        private readonly ScreenCaptureModule _captureModule;
        private readonly Dictionary<string, CachedAnalysis> _analysisCache;
        private readonly object _cacheLock = new object();

        // Coordinate extraction regex pattern
        private static readonly Regex CoordPattern = new Regex(
            @"\[(?:Tıklanacak Koordinat|Click Coordinate|Koordinat):\s*(-?\d+)\s*,\s*(-?\d+)\s*\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        // Retry configuration
        private static readonly double[] RetryDelays = new[] { 0.9, 1.8, 3.0 };
        private const int MaxRetriesPerModel = 3;

        // Model fallback chain: gemini-2.5-flash → gemini-2.5-flash-lite → gemini-2.0-flash
        private static readonly string[] DefaultModelChain = new[]
        {
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-2.0-flash"
        };

        private class CachedAnalysis
        {
            public VisionAnalysisResult Result { get; set; } = null!;
            public DateTime Timestamp { get; set; }
        }

        public VisionEngineService()
        {
            _captureModule = new ScreenCaptureModule();
            _analysisCache = new Dictionary<string, CachedAnalysis>();
        }

        public async Task<VisionAnalysisResult> AnalyzeTargetAsync(
            string query,
            string targetSpec,
            VisionOptions? options = null)
        {
            options ??= new VisionOptions();

            // Check cache first
            if (options.UseCache)
            {
                var cached = GetCachedAnalysis(targetSpec);
                if (cached != null)
                {
                    Logger.Information($"[VisionEngine] Using cached analysis for '{targetSpec}'");
                    return cached;
                }
            }

            // Capture the target
            var captureResult = CaptureTarget(targetSpec);
            if (!captureResult.Success || captureResult.Image == null)
            {
                Logger.Error($"[VisionEngine] Failed to capture target '{targetSpec}': {captureResult.ErrorMessage}");
                throw new InvalidOperationException($"Failed to capture target: {captureResult.ErrorMessage}");
            }

            try
            {
                // Save capture to temporary file for API upload
                var tempPath = _captureModule.SaveCapture(captureResult);
                if (tempPath == null)
                {
                    throw new InvalidOperationException("Failed to save capture to temporary file");
                }

                try
                {
                    // Convert ScreenCaptureModule.CaptureContext to VisionEngineService.CaptureContext
                    var context = ConvertCaptureContext(captureResult.Context);

                    // Analyze with Gemini Vision API using retry and fallback logic
                    var analysisText = await AnalyzeWithGeminiAsync(query, context, tempPath, options);

                    // Parse the analysis result
                    var result = ParseAnalysisResult(analysisText, context, options);

                    // Cache the result
                    if (options.UseCache)
                    {
                        CacheAnalysis(targetSpec, result, options.CacheDuration);
                    }

                    return result;
                }
                finally
                {
                    // Clean up temporary file
                    try { File.Delete(tempPath); } catch { }
                }
            }
            finally
            {
                // Clean up capture bitmap
                captureResult.Image?.Dispose();
            }
        }

        public async Task<OcrResult> ExtractTextAsync(Rectangle region, string language = "tr")
        {
            // TODO: Implement OCR text extraction using Windows OCR API or Tesseract
            // For now, throw NotImplementedException as this is not part of task 3.3
            throw new NotImplementedException("OCR text extraction will be implemented in a future task");
        }

        public Point TransformToAbsolute(Point relativeCoord, CaptureContext context)
        {
            // Transform relative coordinates (within screenshot) to absolute screen coordinates
            int absoluteX = context.Offset.X + relativeCoord.X;
            int absoluteY = context.Offset.Y + relativeCoord.Y;

            Logger.Debug($"[VisionEngine] Transformed relative ({relativeCoord.X}, {relativeCoord.Y}) " +
                        $"to absolute ({absoluteX}, {absoluteY}) using offset ({context.Offset.X}, {context.Offset.Y})");

            return new Point(absoluteX, absoluteY);
        }

        public VisionAnalysisResult? GetCachedAnalysis(string targetSpec)
        {
            lock (_cacheLock)
            {
                if (_analysisCache.TryGetValue(targetSpec, out var cached))
                {
                    // Check if cache is still valid
                    var age = DateTime.UtcNow - cached.Timestamp;
                    if (age.TotalSeconds <= 2) // Default cache duration from interface
                    {
                        return cached.Result;
                    }
                    else
                    {
                        // Remove expired cache entry
                        _analysisCache.Remove(targetSpec);
                    }
                }
            }

            return null;
        }

        #region Private Helper Methods

        private CaptureContext ConvertCaptureContext(ScreenCaptureModule.CaptureContext sourceContext)
        {
            return new CaptureContext
            {
                TargetTitle = sourceContext.TargetTitle,
                Offset = sourceContext.Offset,
                CaptureSize = sourceContext.CaptureSize,
                TargetType = sourceContext.TargetType,
                WindowHandle = sourceContext.WindowHandle
            };
        }

        private ScreenCaptureModule.CaptureResult CaptureTarget(string targetSpec)
        {
            // Parse target specification and capture accordingly
            // Supported formats: "active_window", "cursor_monitor", "all_monitors", "monitor_0", "monitor_1", etc.

            if (targetSpec.StartsWith("monitor_", StringComparison.OrdinalIgnoreCase))
            {
                // Extract monitor index
                if (int.TryParse(targetSpec.Substring(8), out int monitorIndex))
                {
                    return _captureModule.CaptureMonitor(monitorIndex);
                }
            }

            switch (targetSpec.ToLowerInvariant())
            {
                case "all_monitors":
                case "virtual_desktop":
                    return _captureModule.CaptureVirtualDesktop();

                case "cursor_monitor":
                case "primary_monitor":
                    // For now, capture monitor 0 (primary)
                    return _captureModule.CaptureMonitor(0);

                case "active_window":
                default:
                    // TODO: Integrate with ContextManagerService to get active window handle
                    // For now, capture primary monitor as fallback
                    Logger.Warning($"[VisionEngine] Active window capture not yet implemented, falling back to primary monitor");
                    return _captureModule.CaptureMonitor(0);
            }
        }

        private async Task<string> AnalyzeWithGeminiAsync(
            string query,
            CaptureContext context,
            string imagePath,
            VisionOptions options)
        {
            var apiKey = AppConfig.GetValue("gemini_api_key", "");
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Gemini API key not configured");
            }

            // Read image and convert to base64
            var imageBytes = await File.ReadAllBytesAsync(imagePath);
            var base64Image = Convert.ToBase64String(imageBytes);

            // Build vision prompt
            var prompt = BuildVisionPrompt(query, context);

            // Determine model chain (use options or default)
            var modelChain = options.PreferredModels?.Length > 0 ? options.PreferredModels : DefaultModelChain;

            Exception? lastError = null;

            // Try each model in the fallback chain
            foreach (var modelName in modelChain)
            {
                Logger.Information($"[VisionEngine] Trying model: {modelName}");

                // Retry up to MaxRetriesPerModel times per model with exponential backoff
                for (int attempt = 0; attempt < MaxRetriesPerModel; attempt++)
                {
                    try
                    {
                        var result = await CallGeminiVisionApiAsync(apiKey, modelName, prompt, base64Image);

                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            Logger.Information($"[VisionEngine] Successfully analyzed with {modelName} (attempt {attempt + 1})");
                            return result;
                        }

                        throw new InvalidOperationException("Gemini returned empty response");
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;

                        // Check if this is a transient error worth retrying
                        if (attempt < MaxRetriesPerModel - 1 && IsTransientError(ex))
                        {
                            // Apply exponential backoff delay
                            var delay = TimeSpan.FromSeconds(RetryDelays[attempt]);
                            Logger.Warning($"[VisionEngine] Transient error with {modelName} (attempt {attempt + 1}/{MaxRetriesPerModel}): {ex.Message}. Retrying in {delay.TotalSeconds}s...");
                            await Task.Delay(delay);
                            continue;
                        }

                        // If this is a transient error and we've exhausted retries for this model, try next model
                        if (IsTransientError(ex))
                        {
                            Logger.Warning($"[VisionEngine] All retries exhausted for {modelName}. Trying next model in fallback chain...");
                            break; // Break retry loop, continue to next model
                        }

                        // Non-transient error, fail immediately (don't try other models)
                        throw new InvalidOperationException($"Gemini vision API error: {ex.Message}", ex);
                    }
                }
            }

            // All models and retries exhausted
            var errorMsg = lastError?.Message ?? "All models failed";
            Logger.Error($"[VisionEngine] All models and retries exhausted. Last error: {errorMsg}");
            throw new InvalidOperationException($"Gemini vision analysis failed after all retries: {errorMsg}", lastError);
        }

        private async Task<string> CallGeminiVisionApiAsync(
            string apiKey,
            string modelName,
            string prompt,
            string base64Image)
        {
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new { inline_data = new { mime_type = "image/png", data = base64Image } }
                        }
                    }
                }
            };

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

            var response = await client.PostAsync(url, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            // Check if response indicates an error
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"API returned {response.StatusCode}: {responseJson}");
            }

            using var responseDoc = JsonDocument.Parse(responseJson);

            // Extract text from response
            return ExtractResponseText(responseDoc);
        }

        private bool IsTransientError(Exception ex)
        {
            // Check exception types: ServerError (represented as HttpRequestException in .NET), 
            // TimeoutError, TaskCanceledException
            if (ex is TaskCanceledException || ex is TimeoutException || ex is HttpRequestException)
                return true;

            // Check error message for transient patterns: 503, 429, "unavailable", "overloaded"
            var msg = ex.Message.ToLowerInvariant();
            return msg.Contains("503") || msg.Contains("429") || msg.Contains("timeout") ||
                   msg.Contains("unavailable") || msg.Contains("overloaded") || msg.Contains("try again") ||
                   msg.Contains("servererror") || msg.Contains("rate limit");
        }

        private string BuildVisionPrompt(string query, CaptureContext context)
        {
            // Build Turkish-optimized vision prompt with coordinate guidance
            var prompt = $"Sen Windows JARVIS ekran analiz modülüsün.\n" +
                         $"Yakalanan bölge: {context.TargetTitle}\n" +
                         $"Hedef mod: {context.TargetType}\n" +
                         $"Görüntü boyutu: {context.CaptureSize.Width} x {context.CaptureSize.Height} piksel.\n" +
                         $"Bu görüntünün sol-üst köşesi SANAL MASAÜSTÜNDE ({context.Offset.X}, {context.Offset.Y}) konumunda.\n\n" +
                         "Kurallar:\n" +
                         "1. Ekrandaki metinleri, butonları, hataları oku ve soruyu yanıtla.\n" +
                         "2. Tıklama koordinatı isteniyorsa butonun MERKEZ noktasını ver.\n" +
                         "3. Koordinatlar YALNIZCA bu görüntüye göre (0,0 = sol-üst) olmalı:\n" +
                         $"   0 <= X < {context.CaptureSize.Width}, 0 <= Y < {context.CaptureSize.Height}\n" +
                         "4. Yanıtın SON satırında tam olarak şu formatı kullan:\n" +
                         "   `[Tıklanacak Koordinat: X, Y]`\n" +
                         "5. Emin değilsen tahmin etme; 'net göremiyorum' de.\n\n" +
                         $"Kullanıcı sorusu: {query}\n\n" +
                         "Türkçe, net yanıt ver.";

            // Optimize prompt length (stay under 1500 characters as per Requirement 13.5)
            if (prompt.Length > 1500)
            {
                Logger.Warning($"[VisionEngine] Prompt length ({prompt.Length}) exceeds 1500 characters, truncating...");
                prompt = prompt.Substring(0, 1497) + "...";
            }

            return prompt;
        }

        private string ExtractResponseText(JsonDocument responseDoc)
        {
            try
            {
                var text = responseDoc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString() ?? "";

                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
            catch
            {
                // Fall through to alternative extraction
            }

            // Try alternative extraction from candidates
            try
            {
                var candidates = responseDoc.RootElement.GetProperty("candidates");
                var textParts = new List<string>();

                foreach (var candidate in candidates.EnumerateArray())
                {
                    if (candidate.TryGetProperty("content", out var content) &&
                        content.TryGetProperty("parts", out var parts))
                    {
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textProp))
                            {
                                var partText = textProp.GetString();
                                if (!string.IsNullOrWhiteSpace(partText))
                                    textParts.Add(partText);
                            }
                        }
                    }
                }

                return string.Join("\n", textParts);
            }
            catch
            {
                return "";
            }
        }

        private VisionAnalysisResult ParseAnalysisResult(
            string analysisText,
            CaptureContext context,
            VisionOptions options)
        {
            var result = new VisionAnalysisResult
            {
                AnalysisText = analysisText,
                Context = context,
                Timestamp = DateTime.UtcNow
            };

            // Extract coordinates and create detected elements
            var matches = CoordPattern.Matches(analysisText);

            foreach (Match match in matches)
            {
                try
                {
                    int relativeX = int.Parse(match.Groups[1].Value);
                    int relativeY = int.Parse(match.Groups[2].Value);

                    // Clamp coordinates to captured region bounds
                    relativeX = Math.Max(0, Math.Min(context.CaptureSize.Width - 1, relativeX));
                    relativeY = Math.Max(0, Math.Min(context.CaptureSize.Height - 1, relativeY));

                    // Transform to absolute screen coordinates
                    var relativePoint = new Point(relativeX, relativeY);
                    var absolutePoint = TransformToAbsolute(relativePoint, context);

                    // Create detected element
                    var element = new DetectedElement
                    {
                        Name = "Detected Element",
                        Description = ExtractElementDescription(analysisText, match.Index),
                        AbsoluteCoordinate = absolutePoint,
                        ConfidenceScore = 85, // Default confidence for coordinate matches
                        Bounds = new Rectangle(absolutePoint.X - 20, absolutePoint.Y - 20, 40, 40)
                    };

                    // Apply minimum confidence filter
                    if (element.ConfidenceScore >= options.MinConfidenceScore)
                    {
                        result.Elements.Add(element);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[VisionEngine] Failed to parse coordinate from match '{match.Value}': {ex.Message}");
                }
            }

            Logger.Information($"[VisionEngine] Parsed {result.Elements.Count} elements from analysis");
            return result;
        }

        private string ExtractElementDescription(string text, int coordinateIndex)
        {
            // Extract description from text around the coordinate match
            // Look backwards from coordinate to find element description
            int startIndex = Math.Max(0, coordinateIndex - 200);
            int length = Math.Min(200, coordinateIndex - startIndex);

            if (length > 0)
            {
                var snippet = text.Substring(startIndex, length).Trim();
                // Take the last sentence or phrase
                var sentences = snippet.Split(new[] { '.', '\n', '!' }, StringSplitOptions.RemoveEmptyEntries);
                if (sentences.Length > 0)
                {
                    return sentences.Last().Trim();
                }
            }

            return "UI Element";
        }

        private void CacheAnalysis(string targetSpec, VisionAnalysisResult result, TimeSpan duration)
        {
            lock (_cacheLock)
            {
                _analysisCache[targetSpec] = new CachedAnalysis
                {
                    Result = result,
                    Timestamp = DateTime.UtcNow
                };

                Logger.Debug($"[VisionEngine] Cached analysis for '{targetSpec}' (duration: {duration.TotalSeconds}s)");
            }
        }

        #endregion
    }
}
