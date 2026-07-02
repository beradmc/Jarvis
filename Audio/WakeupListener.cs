using System;
using Vosk;
using JarvisCSharp.Core;
using NAudio.Wave;
using System.IO;

namespace JarvisCSharp.Audio
{
    /// <summary>
    /// Wake word (Vosk AI) + double clap detection.
    ///
    /// Vosk Offline Yapay Zeka: Saniyede yüzlerce kez analiz yaparak fısıltıyı bile yakalar.
    ///
    /// Clap algoritması:
    ///   - RMS eşiği aşıldığında "clap başladı" sayılır.
    ///   - RMS eşiğin altına düştüğünde "clap bitti" sayılır.
    ///   - Birinci clap bitince zamanlayıcı başlar.
    ///   - Belirlenen pencere içinde ikinci clap gelirse double clap onaylanır.
    ///   - İki clap arasında en az SilenceMinMs sessizlik şarttır.
    /// </summary>
    public class WakeupListener : IDisposable
    {
        private Model?                   _voskModel;
        private VoskRecognizer?          _recognizer;
        private WaveInEvent?             _waveIn;
        private bool                     _isRunning;

        // ── Clap state ────────────────────────────────────────────────────────
        private bool     _inClap          = false;
        private DateTime _clapStartTime   = DateTime.MinValue;
        private DateTime _firstClapEndTime = DateTime.MinValue;
        private bool     _waitingSecond   = false;

        private const double RmsThreshold     = 2000.0; // Clap eşiği
        private const double ClapMaxDurMs     = 400.0;  // Bu kadardan uzun ses clap değil
        private const double SilenceMinMs     = 60.0;   // İki clap arası min sessizlik
        private const double DoubleClapWindow = 1.8;    // İki clap arası max süre (sn)
        private const double CooldownSeconds  = 2.5;    // Tetiklemeden sonra bekleme

        private DateTime _suppressUntil = DateTime.MinValue;

        public event EventHandler? WakeWordDetected;

        // ── Public API ────────────────────────────────────────────────────────

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            try   { InitializeVoskRecognizer(); }
            catch (Exception ex)
            {
                Logger.Warning($"Vosk AI init failed: {ex.Message}. Clap-only mode.");
            }

            try   { InitializeClapDetector(); }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize clap detector");
            }

            Logger.Information("Wakeup listener started (Vosk / Double Clap)");
        }

        public void Stop()
        {
            _isRunning = false;
            try { _recognizer?.Dispose(); } catch { }
            _recognizer = null;
            try { _voskModel?.Dispose(); } catch { }
            _voskModel = null;
            try { _waveIn?.StopRecording(); _waveIn?.Dispose(); } catch { }
            _waveIn = null;
            Logger.Information("Wakeup listener stopped");
        }

        public void PauseClapDetector()
        {
            try { _waveIn?.StopRecording(); } catch { }
            Reset();
            Logger.Information("Wakeup microphone paused.");
        }

        public void ResumeClapDetector()
        {
            try
            {
                _waveIn?.StartRecording();
                Logger.Information("Wakeup microphone resumed.");
            }
            catch
            {
                try { _waveIn?.Dispose(); } catch { }
                _waveIn = null;
                try { InitializeClapDetector(); } catch { }
            }
        }

        // ── Vosk AI ───────────────────────────────────────────────────────────

        private void InitializeVoskRecognizer()
        {
            try
            {
                Vosk.Vosk.SetLogLevel(-1); // Suppress verbose Vosk logs

                // ── Smart Model Path Resolution ──────────────────────────────
                // App runs from bin/Debug/net8.0-windows/ but model is in project root /Models/
                // Try multiple strategies to find the model folder.
                string modelName = "vosk-model-small-tr-0.3";
                string? modelPath = null;
                
                string[] searchPaths = new[]
                {
                    // 1. Directly next to exe (if model was copied to output)
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", modelName),
                    // 2. Three levels up from bin/Debug/net8.0-windows → project root
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Models", modelName),
                    // 3. Current working directory
                    Path.Combine(Directory.GetCurrentDirectory(), "Models", modelName),
                    // 4. Three levels up from CWD
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "Models", modelName),
                };

                foreach (var candidate in searchPaths)
                {
                    string fullPath = Path.GetFullPath(candidate); // Normalize ../..
                    Logger.Information($"Vosk model search: checking {fullPath}");
                    if (Directory.Exists(fullPath))
                    {
                        modelPath = fullPath;
                        break;
                    }
                }

                if (modelPath == null)
                {
                    Logger.Warning($"❌ Vosk model '{modelName}' not found in any search path. Voice wake word DISABLED.");
                    return;
                }

                Logger.Information($"✅ Vosk model found at: {modelPath}");
                _voskModel = new Model(modelPath);
                _recognizer = new VoskRecognizer(_voskModel, 16000.0f);
                
                Logger.Information("✅ Vosk AI wake word engine initialized successfully!");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "❌ Failed to initialize VoskRecognizer");
            }
        }

        private bool DetectWakeWordInText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var lower = text.ToLowerInvariant();
            return lower.Contains("jarvis") || lower.Contains("carvis") || 
                   lower.Contains("jervis");
        }

        // ── Clap detector ─────────────────────────────────────────────────────

        private void InitializeClapDetector()
        {
            _waveIn = new WaveInEvent
            {
                WaveFormat         = new WaveFormat(16000, 1),
                BufferMilliseconds = 20  // 20ms granülarite
            };
            _waveIn.DataAvailable += OnAudio;
            _waveIn.StartRecording();
        }

        private void OnAudio(object? sender, WaveInEventArgs e)
        {
            if (!_isRunning) return;

            // ── Vosk AI Ses Analizi ───────────────────────────────────────────
            if (_recognizer != null)
            {
                if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                {
                    string result = _recognizer.Result();
                    if (DetectWakeWordInText(result))
                    {
                        Logger.Information($"Wake word (Vosk Result): '{result}'");
                        Fire();
                    }
                }
                else
                {
                    string partial = _recognizer.PartialResult();
                    if (DetectWakeWordInText(partial))
                    {
                        Logger.Information($"Wake word (Vosk Partial): '{partial}'");
                        Fire();
                    }
                }
            }

            double rms = CalcRms(e.Buffer, e.BytesRecorded);
            var    now = DateTime.UtcNow;

            // ── Zaman aşımı kontrolü ──────────────────────────────────────────
            if (_waitingSecond && (now - _firstClapEndTime).TotalSeconds > DoubleClapWindow)
            {
                // İkinci clap hiç gelmedi
                Logger.Debug("Clap timeout — reset");
                Reset();
            }

            // ── Ses açık (clap devam ediyor) ──────────────────────────────────
            if (rms >= RmsThreshold)
            {
                if (!_inClap)
                {
                    _inClap       = true;
                    _clapStartTime = now;
                }
                else if ((now - _clapStartTime).TotalMilliseconds > ClapMaxDurMs)
                {
                    // Çok uzun süren ses — clap değil (konuşma/gürültü)
                    Logger.Debug($"Long noise ({(now - _clapStartTime).TotalMilliseconds:F0}ms) — reset");
                    Reset();
                }
            }
            // ── Ses kapandı (clap bitti, sessizlik başladı) ───────────────────
            else
            {
                if (_inClap)
                {
                    _inClap = false;
                    double clapDurMs = (now - _clapStartTime).TotalMilliseconds;

                    if (!_waitingSecond)
                    {
                        // Birinci clap bitti
                        _firstClapEndTime = now;
                        _waitingSecond    = true;
                        Logger.Information($"Clap 1 detected ({clapDurMs:F0}ms)");
                    }
                    else
                    {
                        // İkinci clap bitti — aralarındaki sessizliği kontrol et
                        double silenceMs = (_clapStartTime - _firstClapEndTime).TotalMilliseconds;
                        double gapSec    = (now - _firstClapEndTime).TotalSeconds;

                        bool silenceOk = silenceMs >= SilenceMinMs;
                        bool windowOk  = gapSec    <= DoubleClapWindow;

                        Logger.Information($"Clap 2 detected ({clapDurMs:F0}ms) | silence={silenceMs:F0}ms | gap={gapSec:F2}s");

                        if (silenceOk && windowOk)
                        {
                            Logger.Information("✅ Double clap confirmed!");
                            Reset();
                            Fire();
                        }
                        else
                        {
                            // Geçersiz — bu clap'i yeni birinci clap say
                            _firstClapEndTime = now;
                            Logger.Debug($"Invalid double clap (silence={silenceMs:F0}ms, gap={gapSec:F2}s) — restarting");
                        }
                    }
                }
            }
        }

        // ── Yardımcı ─────────────────────────────────────────────────────────

        private void Reset()
        {
            _inClap         = false;
            _waitingSecond  = false;
            _firstClapEndTime = DateTime.MinValue;
        }

        private void Fire()
        {
            if (DateTime.UtcNow < _suppressUntil) return;
            _suppressUntil = DateTime.UtcNow.AddSeconds(CooldownSeconds);
            WakeWordDetected?.Invoke(this, EventArgs.Empty);
        }

        private static double CalcRms(byte[] buf, int len)
        {
            if (len < 2) return 0;
            long sum = 0;
            int  cnt = len / 2;
            for (int i = 0; i < len - 1; i += 2)
            {
                short s = BitConverter.ToInt16(buf, i);
                sum += (long)s * s;
            }
            return Math.Sqrt((double)sum / cnt);
        }

        public void Dispose() => Stop();
    }
}
