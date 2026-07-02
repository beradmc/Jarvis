using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Speech.Synthesis;
using NAudio.Wave;
using JarvisCSharp.Core;
using JarvisCSharp.Config;

namespace JarvisCSharp.Audio
{
    /// <summary>
    /// TTS Service — iki katmanlı:
    ///   1) Gemini TTS API (Charon sesi, akıcı AI sesi)
    ///   2) Windows SAPI Fallback (çevrimdışı)
    /// </summary>
    public class TtsService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly SpeechSynthesizer? _sapi;
        private WaveOutEvent? _waveOut;
        private bool _isSpeaking = false;

        public bool IsSpeaking => _isSpeaking;
        public event EventHandler? SpeakingStarted;
        public event EventHandler? SpeakingFinished;

        public TtsService()
        {
            _apiKey = AppConfig.GetValue("gemini_api_key", "");
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            // Windows SAPI fallback
            try
            {
                _sapi = new SpeechSynthesizer();
                _sapi.Rate = 1;   // -10 (çok yavaş) ile +10 (çok hızlı)
                _sapi.Volume = 90;
                Logger.Information("SAPI TTS fallback ready.");
            }
            catch (Exception ex)
            {
                Logger.Warning($"SAPI TTS not available: {ex.Message}");
                _sapi = null;
            }
        }

        /// <summary>
        /// Verilen metni seslendirir. Önce Gemini TTS dener, olmazsa SAPI kullanır.
        /// </summary>
        public async Task SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_isSpeaking) StopSpeaking();

            _isSpeaking = true;
            SpeakingStarted?.Invoke(this, EventArgs.Empty);
            Logger.Information($"TTS: \"{text.Substring(0, Math.Min(60, text.Length))}...\"");

            try
            {
                bool success = !string.IsNullOrEmpty(_apiKey) && await TrySpeakWithGeminiAsync(text);
                if (!success)
                {
                    SpeakWithSapi(text);
                }
            }
            finally
            {
                _isSpeaking = false;
                SpeakingFinished?.Invoke(this, EventArgs.Empty);
            }
        }

        // ── Gemini TTS (REST API) ─────────────────────────────────────────────
        private async Task<bool> TrySpeakWithGeminiAsync(string text)
        {
            try
            {
                // Gemini 2.5 Flash Preview TTS
                string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-preview-tts:generateContent?key={_apiKey}";

                var payload = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = text } } }
                    },
                    generationConfig = new
                    {
                        responseModalities = new[] { "AUDIO" },
                        speechConfig = new
                        {
                            voiceConfig = new
                            {
                                prebuiltVoiceConfig = new { voiceName = "Charon" }
                            }
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.Warning($"Gemini TTS API error: {response.StatusCode}");
                    return false;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);

                // Extract inline audio data (base64 PCM)
                var root = doc.RootElement;
                var candidates = root.GetProperty("candidates");
                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                var inlineData = parts[0].GetProperty("inlineData");
                var mimeType = inlineData.GetProperty("mimeType").GetString() ?? "";
                var b64Data = inlineData.GetProperty("data").GetString() ?? "";

                if (string.IsNullOrEmpty(b64Data))
                {
                    Logger.Warning("Gemini TTS: empty audio data.");
                    return false;
                }

                byte[] audioBytes = Convert.FromBase64String(b64Data);

                // Parse sampleRate from mimeType: "audio/L16;codec=pcm;rate=24000"
                int sampleRate = 24000;
                foreach (var part in mimeType.Split(';'))
                {
                    var kv = part.Trim().Split('=');
                    if (kv.Length == 2 && kv[0].Trim() == "rate")
                    {
                        int.TryParse(kv[1].Trim(), out sampleRate);
                    }
                }

                PlayPcm16(audioBytes, sampleRate);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Gemini TTS failed: {ex.Message}. Falling back to SAPI.");
                return false;
            }
        }

        // ── PCM16 Playback via NAudio ────────────────────────────────────────
        private void PlayPcm16(byte[] pcmData, int sampleRate)
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();

            var waveFormat = new WaveFormat(sampleRate, 16, 1); // 16-bit, mono
            var ms = new MemoryStream(pcmData);
            var rawStream = new RawSourceWaveStream(ms, waveFormat);

            _waveOut = new WaveOutEvent();
            _waveOut.Init(rawStream);
            _waveOut.PlaybackStopped += (s, e) =>
            {
                ms.Dispose();
                rawStream.Dispose();
            };
            _waveOut.Play();

            // Block thread until done (SpeakAsync handles async context)
            while (_waveOut.PlaybackState == PlaybackState.Playing)
            {
                System.Threading.Thread.Sleep(50);
            }
        }

        // ── Windows SAPI Fallback ────────────────────────────────────────────
        private void SpeakWithSapi(string text)
        {
            if (_sapi == null)
            {
                Logger.Warning("No TTS available (no API key and SAPI not installed).");
                return;
            }

            Logger.Information("Using Windows SAPI TTS fallback.");
            _sapi.Speak(text);
        }

        public void StopSpeaking()
        {
            _waveOut?.Stop();
            _sapi?.SpeakAsyncCancelAll();
            _isSpeaking = false;
        }

        public void Dispose()
        {
            StopSpeaking();
            _waveOut?.Dispose();
            _sapi?.Dispose();
            _httpClient.Dispose();
        }
    }
}
