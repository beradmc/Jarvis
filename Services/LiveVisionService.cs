using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using JarvisCSharp.AI;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services
{
    public class LiveVisionService : IDisposable
    {
        private readonly GeminiService _geminiService;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        
        // P/Invoke for screen capture
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

        public LiveVisionService(GeminiService geminiService)
        {
            _geminiService = geminiService;
        }

        public void StartStreaming()
        {
            if (_loopTask != null && !_loopTask.IsCompleted) return;

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => CaptureLoopAsync(_cts.Token), _cts.Token);
            Logger.Information("Live Vision streaming started.");
        }

        public void StopStreaming()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
                Logger.Information("Live Vision streaming stopped.");
            }
        }

        private async Task CaptureLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // If Gemini session is not open, just wait and try again later
                    if (!_geminiService.IsSessionOpen)
                    {
                        await Task.Delay(2000, token);
                        continue;
                    }

                    byte[]? jpegData = CaptureScreenAsJpeg();
                    if (jpegData != null)
                    {
                        await _geminiService.SendVideoFrameAsync(jpegData);
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Live Vision loop error: {ex.Message}");
                }

                // Wait 1.5 seconds between frames to balance responsiveness and bandwidth (approx 0.6 FPS)
                await Task.Delay(1500, token);
            }
        }

        private byte[]? CaptureScreenAsJpeg()
        {
            try
            {
                int w = GetSystemMetrics(78); // SM_CXVIRTUALSCREEN
                int h = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
                int x = GetSystemMetrics(76); // SM_XVIRTUALSCREEN
                int y = GetSystemMetrics(77); // SM_YVIRTUALSCREEN

                if (w <= 0 || h <= 0)
                {
                    var screen = Screen.PrimaryScreen;
                    if (screen == null) return null;
                    w = screen.Bounds.Width;
                    h = screen.Bounds.Height;
                    x = screen.Bounds.X;
                    y = screen.Bounds.Y;
                }

                using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(bmp);
                g.CopyFromScreen(x, y, 0, 0, new Size(w, h));

                // Scale down to max 1024x1024 to save bandwidth and API limits
                int maxDim = 1024;
                if (w > maxDim || h > maxDim)
                {
                    float ratio = Math.Min((float)maxDim / w, (float)maxDim / h);
                    int newW = (int)(w * ratio);
                    int newH = (int)(h * ratio);

                    using var resized = new Bitmap(newW, newH);
                    using var g2 = Graphics.FromImage(resized);
                    g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g2.DrawImage(bmp, 0, 0, newW, newH);

                    return EncodeToJpeg(resized, 60L); // 60% quality
                }

                return EncodeToJpeg(bmp, 60L);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Screen capture failed: {ex.Message}");
                return null;
            }
        }

        private byte[] EncodeToJpeg(Bitmap bmp, long quality)
        {
            var jpegCodec = GetEncoder(ImageFormat.Jpeg);
            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

            using var ms = new MemoryStream();
            bmp.Save(ms, jpegCodec!, encoderParameters);
            return ms.ToArray();
        }

        private ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        public void Dispose()
        {
            StopStreaming();
        }
    }
}
