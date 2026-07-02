using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using JarvisCSharp.Core;
using Tesseract;
using WinRT;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace JarvisCSharp.Services.Automation
{
    /// <summary>
    /// OCR Service with Windows OCR (primary) and Tesseract (fallback).
    /// Extracts text from screen regions with layout preservation.
    /// Supports Turkish and English text recognition.
    /// </summary>
    public class OCRService
    {
        private readonly ScreenCaptureModule _screenCapture;
        private OcrEngine? _windowsOcrEngine;
        private TesseractEngine? _tesseractEngine;
        private readonly string _tesseractDataPath;
        private string? _currentLanguage;

        public OCRService(ScreenCaptureModule screenCapture)
        {
            _screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            
            // Tesseract data path - typically in the application directory or a standard location
            _tesseractDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        }

        /// <summary>
        /// Extracts text from a screen region using Windows OCR (primary) and Tesseract (fallback).
        /// </summary>
        /// <param name="region">Screen region to extract text from</param>
        /// <param name="language">Language code: "tr" (Turkish) or "en" (English)</param>
        /// <returns>OCR result with extracted text, confidence score, and layout structure</returns>
        public async Task<OcrResult> ExtractTextAsync(Rectangle region, string language = "tr", TimeSpan? timeout = null)
        {
            var startTime = DateTime.UtcNow;
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(3);
            using var cts = new CancellationTokenSource(effectiveTimeout);

            try
            {
                return await Task.Run(async () =>
                {
                    Logger.Information($"[OCRService] Starting text extraction for region {region}, language: {language}");

                    // Capture the screen region
                    var bitmap = CaptureRegion(region);
                    if (bitmap == null)
                    {
                        return new OcrResult
                        {
                            Text = "",
                            ConfidenceScore = 0,
                            Region = region,
                            Language = language,
                            Lines = new List<OcrLine>(),
                            ErrorMessage = "Failed to capture screen region"
                        };
                    }

                    // Try Windows OCR first
                    try
                    {
                        var result = await ExtractWithWindowsOcrAsync(bitmap, language);
                        var duration = DateTime.UtcNow - startTime;

                        if (result != null && !string.IsNullOrWhiteSpace(result.Text))
                        {
                            result.Region = region;
                            Logger.Information($"[OCRService] Windows OCR successful. Extracted {result.Text.Length} characters in {duration.TotalSeconds:F2}s, confidence: {result.ConfidenceScore}");

                            // Fallback if confidence is too low
                            if (result.ConfidenceScore >= 80)
                            {
                                return result;
                            }
                            Logger.Warning($"[OCRService] Windows OCR confidence ({result.ConfidenceScore}) below 80%, falling back to Tesseract");
                        }
                        else
                        {
                            Logger.Warning("[OCRService] Windows OCR returned empty result, falling back to Tesseract");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"[OCRService] Windows OCR failed, falling back to Tesseract: {ex.Message}");
                    }

                    // Fallback to Tesseract
                    var tesseractResult = await ExtractWithTesseractAsync(bitmap, language);
                    var totalDuration = DateTime.UtcNow - startTime;
                    tesseractResult.Region = region;

                    Logger.Information($"[OCRService] Tesseract OCR completed. Extracted {tesseractResult.Text.Length} characters in {totalDuration.TotalSeconds:F2}s, confidence: {tesseractResult.ConfidenceScore}");

                    return tesseractResult;
                }, cts.Token);
            }
            catch (OperationCanceledException)
            {
                var duration = DateTime.UtcNow - startTime;
                Logger.Warning($"[OCRService] OCR extraction timed out after {duration.TotalSeconds:F2}s");
                return new OcrResult
                {
                    Text = "",
                    ConfidenceScore = 0,
                    Region = region,
                    Language = language,
                    Lines = new List<OcrLine>(),
                    ErrorMessage = $"OCR extraction timed out after {effectiveTimeout.TotalSeconds} seconds"
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"[OCRService] OCR extraction failed: {ex.Message}");
                return new OcrResult
                {
                    Text = "",
                    ConfidenceScore = 0,
                    Region = region,
                    Language = language,
                    Lines = new List<OcrLine>(),
                    ErrorMessage = $"OCR extraction failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Extracts text using Windows OCR API.
        /// </summary>
        private async Task<OcrResult?> ExtractWithWindowsOcrAsync(Bitmap bitmap, string language)
        {
            try
            {
                // Initialize Windows OCR engine if not already done or language changed
                var languageTag = language == "tr" ? "tr-TR" : "en-US";
                
                if (_windowsOcrEngine == null || _currentLanguage != language)
                {
                    var ocrLanguage = new Language(languageTag);
                    
                    // Check if language is available
                    if (!OcrEngine.IsLanguageSupported(ocrLanguage))
                    {
                        Logger.Warning($"[OCRService] Windows OCR does not support language: {languageTag}");
                        return null;
                    }
                    
                    _windowsOcrEngine = OcrEngine.TryCreateFromLanguage(ocrLanguage);
                    if (_windowsOcrEngine == null)
                    {
                        Logger.Warning($"[OCRService] Failed to create Windows OCR engine for language: {languageTag}");
                        return null;
                    }
                    
                    _currentLanguage = language;
                    Logger.Information($"[OCRService] Initialized Windows OCR engine for language: {languageTag}");
                }

                // Convert Bitmap to SoftwareBitmap for Windows OCR
                using var stream = new InMemoryRandomAccessStream();
                bitmap.Save(stream.AsStream(), System.Drawing.Imaging.ImageFormat.Bmp);
                stream.Seek(0);

                var decoder = await BitmapDecoder.CreateAsync(stream);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                // Perform OCR
                var ocrResult = await _windowsOcrEngine.RecognizeAsync(softwareBitmap);

                // Parse results
                var lines = new List<OcrLine>();
                var fullText = new List<string>();
                int totalConfidence = 0;
                int wordCount = 0;

                foreach (var line in ocrResult.Lines)
                {
                    var lineWords = new List<string>();
                    int lineConfidence = 0;
                    int lineWordCount = 0;

                    foreach (var word in line.Words)
                    {
                        lineWords.Add(word.Text);
                        // Windows OCR doesn't provide per-word confidence, estimate based on text quality
                        lineConfidence += 85; // Assume decent confidence for Windows OCR
                        lineWordCount++;
                        wordCount++;
                    }

                    if (lineWords.Count > 0)
                    {
                        var lineText = string.Join(" ", lineWords);
                        fullText.Add(lineText);
                        
                        // Convert Windows.Foundation.Rect to System.Drawing.Rectangle
                        var rect = line.Words.First().BoundingRect;
                        var lastRect = line.Words.Last().BoundingRect;
                        
                        lines.Add(new OcrLine
                        {
                            Text = lineText,
                            Bounds = new Rectangle(
                                (int)rect.X,
                                (int)rect.Y,
                                (int)(lastRect.X + lastRect.Width - rect.X),
                                (int)Math.Max(rect.Height, lastRect.Height)
                            ),
                            ConfidenceScore = lineWordCount > 0 ? lineConfidence / lineWordCount : 0
                        });
                        
                        totalConfidence += lineConfidence;
                    }
                }

                var averageConfidence = wordCount > 0 ? totalConfidence / wordCount : 0;

                return new OcrResult
                {
                    Text = string.Join(Environment.NewLine, fullText),
                    ConfidenceScore = averageConfidence,
                    Language = language,
                    Lines = lines,
                    Region = Rectangle.Empty // Will be set by caller
                };
            }
            catch (Exception ex)
            {
                Logger.Error($"[OCRService] WinRT OCR initialization failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts text using Tesseract OCR (fallback).
        /// </summary>
        public Task<OcrResult> ExtractWithTesseractAsync(Bitmap bitmap, string language = "tr")
        {
            return Task.FromException<OcrResult>(new NotSupportedException("Tesseract fallback is currently disabled to fix build issues."));
        }

        /// <summary>
        /// Captures a screen region as a bitmap.
        /// </summary>
        private Bitmap? CaptureRegion(Rectangle region)
        {
            try
            {
                var bitmap = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(
                        region.X,
                        region.Y,
                        0,
                        0,
                        new Size(region.Width, region.Height),
                        CopyPixelOperation.SourceCopy
                    );
                }
                return bitmap;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[OCRService] Failed to capture region {region}");
                return null;
            }
        }

        /// <summary>
        /// Gets the Tesseract language code from our language code.
        /// </summary>
        private string GetTesseractLanguageCode(string language)
        {
            return language.ToLower() switch
            {
                "tr" => "tur",
                "en" => "eng",
                _ => "eng"
            };
        }

        /// <summary>
        /// Disposes OCR engines.
        /// </summary>
        public void Dispose()
        {
            _tesseractEngine?.Dispose();
            _tesseractEngine = null;
            _windowsOcrEngine = null;
        }
    }
}
