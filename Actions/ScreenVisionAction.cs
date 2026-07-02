using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using JarvisCSharp.Config;
using JarvisCSharp.Core;

namespace JarvisCSharp.Actions
{
    public class ScreenVisionAction : IAction
    {
        public string Name => "analyze_screen";
        public string Description => "Ekran görüntüsü alıp Gemini vision ile analiz eder.";

        [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern int GetWindowTextLengthW(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
        
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }
        
        private static IntPtr _lastUserHwnd = IntPtr.Zero;
        
        // Coordinate extraction regex pattern
        private static readonly Regex CoordPattern = new Regex(
            @"\[(?:Tıklanacak Koordinat|Click Coordinate|Koordinat):\s*(-?\d+)\s*,\s*(-?\d+)\s*\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );
        
        private static readonly Dictionary<string, string> TargetAliases = new()
        {
            ["active_window"] = "active_window",
            ["window"] = "active_window",
            ["pencere"] = "active_window",
            ["cursor_monitor"] = "cursor_monitor",
            ["monitor"] = "cursor_monitor",
            ["monitör"] = "cursor_monitor",
            ["this_monitor"] = "cursor_monitor",
            ["primary"] = "primary_monitor",
            ["primary_monitor"] = "primary_monitor",
            ["secondary"] = "secondary_monitor",
            ["secondary_monitor"] = "secondary_monitor",
            ["all"] = "all_monitors",
            ["all_monitors"] = "all_monitors",
            ["desktop"] = "all_monitors",
            ["masaüstü"] = "all_monitors",
            ["masaustu"] = "all_monitors",
            ["dual"] = "all_monitors",
            ["both_monitors"] = "all_monitors",
            ["tüm ekran"] = "all_monitors",
            ["tum ekran"] = "all_monitors",
            ["çift monitör"] = "all_monitors",
            ["cift monitor"] = "all_monitors",
        };

        public async Task<string> ExecuteAsync(string payload)
        {
            string query  = "Ekranda ne var?";
            string target = "active_window";

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("query",  out var q)) query  = q.GetString() ?? query;
                if (root.TryGetProperty("target", out var t)) target = t.GetString() ?? target;
            }
            catch { }

            // Remember current user window
            RememberUserWindow();

            try
            {
                // Normalize target
                target = NormalizeTarget(target);

                // Capture screen with specified target
                var captureResult = CaptureForTarget(target);
                if (captureResult == null || captureResult.ImagePath == null)
                    return "Hata: Ekran görüntüsü alınamadı.";

                var result = await AnalyzeWithGemini(query, captureResult);
                Logger.Information($"[ScreenVision] {result[..Math.Min(120, result.Length)]}");
                
                try { File.Delete(captureResult.ImagePath); } catch { }
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ScreenVisionAction failed");
                return $"Hata: Ekran analizi başarısız — {ex.Message}";
            }
        }

        private class CaptureResult
        {
            public string? ImagePath { get; set; }
            public string Title { get; set; } = "";
            public int OffsetX { get; set; }
            public int OffsetY { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string Target { get; set; } = "active_window";
        }

        private static void RememberUserWindow()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero && !IsJarvisWindow(hwnd))
                {
                    _lastUserHwnd = hwnd;
                }
            }
            catch { }
        }

        private static bool IsJarvisWindow(IntPtr hwnd)
        {
            var title = GetWindowTitle(hwnd);
            return title.Contains("Jarvis", StringComparison.OrdinalIgnoreCase) ||
                   title.Contains("JARVIS", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetWindowTitle(IntPtr hwnd)
        {
            int length = GetWindowTextLengthW(hwnd);
            if (length == 0) return "";
            var sb = new StringBuilder(length + 1);
            GetWindowTextW(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string NormalizeTarget(string target)
        {
            var key = (target ?? "active_window").Trim().ToLowerInvariant();
            return TargetAliases.TryGetValue(key, out var normalized) ? normalized : "active_window";
        }

        private static Screen GetCursorMonitor()
        {
            GetCursorPos(out POINT pt);
            return Screen.FromPoint(new Point(pt.X, pt.Y));
        }

        private static IntPtr ResolveActiveWindowHwnd()
        {
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && !IsJarvisWindow(fg))
            {
                _lastUserHwnd = fg;
                return fg;
            }

            if (_lastUserHwnd != IntPtr.Zero && IsWindow(_lastUserHwnd))
            {
                if (!IsJarvisWindow(_lastUserHwnd))
                    return _lastUserHwnd;
            }

            // Find best window on cursor monitor
            var cursorMonitor = GetCursorMonitor();
            return FindBestWindowOnScreen(cursorMonitor);
        }

        private static IntPtr FindBestWindowOnScreen(Screen screen)
        {
            IntPtr bestHwnd = IntPtr.Zero;
            int bestArea = 0;
            var screenRect = screen.Bounds;

            EnumWindows((hwnd, lParam) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                if (IsJarvisWindow(hwnd)) return true;

                GetWindowRect(hwnd, out RECT rect);
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width < 120 || height < 80) return true;

                var intersectArea = GetRectIntersectionArea(
                    rect.Left, rect.Top, width, height,
                    screenRect.X, screenRect.Y, screenRect.Width, screenRect.Height
                );

                if (intersectArea > bestArea)
                {
                    bestArea = intersectArea;
                    bestHwnd = hwnd;
                }

                return true;
            }, IntPtr.Zero);

            return bestHwnd;
        }

        private static int GetRectIntersectionArea(int ax, int ay, int aw, int ah, int bx, int by, int bw, int bh)
        {
            int x1 = Math.Max(ax, bx);
            int y1 = Math.Max(ay, by);
            int x2 = Math.Min(ax + aw, bx + bw);
            int y2 = Math.Min(ay + ah, by + bh);
            if (x2 <= x1 || y2 <= y1) return 0;
            return (x2 - x1) * (y2 - y1);
        }

        private static Rectangle GetWindowClientRect(IntPtr hwnd)
        {
            GetClientRect(hwnd, out RECT clientRect);
            POINT topLeft = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref topLeft);

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;

            return new Rectangle(topLeft.X, topLeft.Y, width, height);
        }

        private static CaptureResult? CaptureForTarget(string target)
        {
            try
            {
                Rectangle captureRect;
                string title;

                switch (target)
                {
                    case "all_monitors":
                        {
                            var virtualBounds = GetVirtualScreenBounds();
                            captureRect = virtualBounds;
                            title = $"Tüm monitörler ({Screen.AllScreens.Length} ekran)";
                            break;
                        }

                    case "cursor_monitor":
                        {
                            var screen = GetCursorMonitor();
                            captureRect = screen.Bounds;
                            title = "Fare monitörü";
                            break;
                        }

                    case "primary_monitor":
                        {
                            var screen = Screen.PrimaryScreen ?? Screen.AllScreens[0];
                            captureRect = screen.Bounds;
                            title = "Birincil monitör";
                            break;
                        }

                    case "secondary_monitor":
                        {
                            var screens = Screen.AllScreens;
                            if (screens.Length <= 1)
                            {
                                captureRect = screens[0].Bounds;
                                title = "Tek monitör (ikincil yok)";
                            }
                            else
                            {
                                captureRect = screens[1].Bounds;
                                title = "İkincil monitör";
                            }
                            break;
                        }

                    default: // active_window
                        {
                            var hwnd = ResolveActiveWindowHwnd();
                            if (hwnd != IntPtr.Zero)
                            {
                                captureRect = GetWindowClientRect(hwnd);
                                title = GetWindowTitle(hwnd);

                                // If window too small or is Jarvis, fall back to cursor monitor
                                if (captureRect.Width <= 100 || captureRect.Height <= 100 || IsJarvisWindow(hwnd))
                                {
                                    var screen = GetCursorMonitor();
                                    var alt = FindBestWindowOnScreen(screen);
                                    if (alt != IntPtr.Zero)
                                    {
                                        captureRect = GetWindowClientRect(alt);
                                        title = GetWindowTitle(alt);
                                    }

                                    if (captureRect.Width <= 100 || captureRect.Height <= 100)
                                    {
                                        captureRect = screen.Bounds;
                                        title = title != "" ? title : "Fare monitörü";
                                    }
                                }
                            }
                            else
                            {
                                var screen = GetCursorMonitor();
                                captureRect = screen.Bounds;
                                title = "Fare monitörü";
                            }
                            break;
                        }
                }

                // Capture the screen region
                using var bmp = new Bitmap(captureRect.Width, captureRect.Height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                g.CopyFromScreen(captureRect.X, captureRect.Y, 0, 0, new Size(captureRect.Width, captureRect.Height));

                var tmpPath = Path.Combine(Path.GetTempPath(), $"jarvis-screen-{Guid.NewGuid():N}.png");
                bmp.Save(tmpPath, ImageFormat.Png);

                return new CaptureResult
                {
                    ImagePath = tmpPath,
                    Title = title,
                    OffsetX = captureRect.X,
                    OffsetY = captureRect.Y,
                    Width = captureRect.Width,
                    Height = captureRect.Height,
                    Target = target
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Screenshot capture failed");
                return null;
            }
        }

        private static Rectangle GetVirtualScreenBounds()
        {
            int x = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
            int y = GetSystemMetrics(77); // SM_YVIRTUALSCREEN
            int w = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
            int h = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN

            if (w <= 0 || h <= 0)
            {
                return Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            }

            return new Rectangle(x, y, w, h);
        }

        private static string? CaptureScreen()
        {
            try
            {
                // GetSystemMetrics ile sanal ekran boyutlarını al
                int w = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
                int h = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
                int x = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
                int y = GetSystemMetrics(77); // SM_YVIRTUALSCREEN

                if (w <= 0 || h <= 0) { w = 1920; h = 1080; x = 0; y = 0; }

                using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using var g   = Graphics.FromImage(bmp);
                g.CopyFromScreen(x, y, 0, 0, new Size(w, h));

                var tmpPath = Path.Combine(Path.GetTempPath(), $"jarvis-screen-{Guid.NewGuid():N}.png");
                bmp.Save(tmpPath, ImageFormat.Png);
                return tmpPath;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Screenshot capture failed");
                return null;
            }
        }

        private static readonly string[] VisionModels = new[]
        {
            "gemini-2.5-flash",
            "gemini-2.5-flash-lite",
            "gemini-2.0-flash"
        };

        private static readonly double[] RetryDelays = new[] { 0.9, 1.8, 3.0 };

        private static bool IsTransientVisionError(Exception ex)
        {
            // Check exception types: ServerError (represented as HttpRequestException in .NET), TimeoutError, TaskCanceledException
            if (ex is TaskCanceledException || ex is TimeoutException || ex is HttpRequestException)
                return true;

            // Check error message for transient patterns: 503, 429, "unavailable", "overloaded"
            var msg = ex.Message.ToLowerInvariant();
            return msg.Contains("503") || msg.Contains("429") || msg.Contains("timeout") ||
                   msg.Contains("unavailable") || msg.Contains("overloaded") || msg.Contains("try again") ||
                   msg.Contains("servererror");
        }

        private static async Task<string> AnalyzeWithGemini(string query, CaptureResult capture)
        {
            var apiKey = AppConfig.GetValue("gemini_api_key", "");
            if (string.IsNullOrEmpty(apiKey))
                return "Gemini API key eksik.";

            var imageBytes  = await File.ReadAllBytesAsync(capture.ImagePath!);
            var base64Image = Convert.ToBase64String(imageBytes);

            // Get virtual screen bounds for context
            var virtualBounds = GetVirtualScreenBounds();

            // Build vision prompt with coordinate guidance
            var prompt = $"Sen Windows JARVIS ekran analiz modülüsün.\n" +
                         $"Yakalanan bölge: {capture.Title}\n" +
                         $"Hedef mod: {capture.Target}\n" +
                         $"Görüntü boyutu: {capture.Width} x {capture.Height} piksel.\n" +
                         $"Bu görüntünün sol-üst köşesi SANAL MASAÜSTÜNDE ({capture.OffsetX}, {capture.OffsetY}) konumunda.\n" +
                         $"Sanal masaüstü: {virtualBounds.Width}x{virtualBounds.Height} (origin {virtualBounds.X},{virtualBounds.Y}).\n\n" +
                         "Kurallar:\n" +
                         "1. Ekrandaki metinleri, butonları, hataları oku ve soruyu yanıtla.\n" +
                         "2. Tıklama koordinatı isteniyorsa butonun MERKEZ noktasını ver.\n" +
                         "3. Koordinatlar YALNIZCA bu görüntüye göre (0,0 = sol-üst) olmalı:\n" +
                         $"   0 <= X < {capture.Width}, 0 <= Y < {capture.Height}\n" +
                         "4. Yanıtın SON satırında tam olarak şu formatı kullan:\n" +
                         "   `[Tıklanacak Koordinat: X, Y]`\n" +
                         "5. Emin değilsen tahmin etme; 'net göremiyorum' de.\n\n" +
                         $"Kullanıcı sorusu: {query}\n\n" +
                         "Türkçe, net yanıt ver.";

            Exception? lastError = null;

            // Try each model in the fallback chain
            foreach (var modelName in VisionModels)
            {
                // Retry up to 3 times per model with exponential backoff
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
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
                        var json    = JsonSerializer.Serialize(requestBody);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var url     = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

                        var response = await client.PostAsync(url, content);
                        var respJson = await response.Content.ReadAsStringAsync();

                        // Check if response indicates an error
                        if (!response.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException($"API returned {response.StatusCode}: {respJson}");
                        }

                        using var respDoc = JsonDocument.Parse(respJson);
                        
                        // Extract text from response
                        var text = ExtractResponseText(respDoc);
                        
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            throw new InvalidOperationException("Gemini geçerli analiz döndürmedi.");
                        }

                        // Transform coordinates in the response
                        text = TransformCoordinatesInText(text, capture);

                        // Add metadata prefix
                        var meta = $"[Yakalanan: {capture.Title} | hedef={capture.Target} | " +
                                   $"ofset=({capture.OffsetX},{capture.OffsetY}) | boyut={capture.Width}x{capture.Height}]\n";

                        return meta + text;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        
                        // Check if this is a transient error worth retrying
                        if (attempt < 2 && IsTransientVisionError(ex))
                        {
                            // Apply exponential backoff delay
                            var delay = TimeSpan.FromSeconds(RetryDelays[attempt]);
                            Logger.Warning($"[ScreenVision] Transient error with {modelName} (attempt {attempt + 1}/3): {ex.Message}. Retrying in {delay.TotalSeconds}s...");
                            await Task.Delay(delay);
                            continue;
                        }
                        
                        // If this is a transient error and we've exhausted retries for this model, try next model
                        if (IsTransientVisionError(ex))
                        {
                            Logger.Warning($"[ScreenVision] All retries failed for {modelName}. Trying next model...");
                            break; // Break retry loop, continue to next model
                        }
                        
                        // Non-transient error, fail immediately
                        throw new InvalidOperationException($"Gemini vision hatası: {ex.Message}", ex);
                    }
                }
            }

            // All models and retries exhausted
            throw new InvalidOperationException($"Gemini vision hatası: {lastError?.Message ?? "Tüm modeller başarısız oldu."}");
        }

        private static string ExtractResponseText(JsonDocument respDoc)
        {
            try
            {
                var text = respDoc.RootElement
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
                var candidates = respDoc.RootElement.GetProperty("candidates");
                var textParts = new System.Collections.Generic.List<string>();
                
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

        private static string TransformCoordinatesInText(string text, CaptureResult capture)
        {
            return CoordPattern.Replace(text, match =>
            {
                try
                {
                    int x = int.Parse(match.Groups[1].Value);
                    int y = int.Parse(match.Groups[2].Value);

                    // Clamp coordinates to captured region bounds
                    x = Math.Max(0, Math.Min(capture.Width - 1, x));
                    y = Math.Max(0, Math.Min(capture.Height - 1, y));

                    // Transform to virtual screen coordinates
                    int screenX = capture.OffsetX + x;
                    int screenY = capture.OffsetY + y;

                    return $"[Tıklanacak Koordinat: {screenX}, {screenY}]";
                }
                catch
                {
                    return match.Value; // Return original if parsing fails
                }
            });
        }
    }
}
