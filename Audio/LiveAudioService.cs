using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using JarvisCSharp.Core;

namespace JarvisCSharp.Audio
{
    /// <summary>
    /// Gerçek zamanlı ses akışı servisi.
    /// Mikrofon → Gemini Live API (SendAudio)
    /// Gemini Live API → Hoparlör (PlayAudio)
    ///
    /// VAD (Voice Activity Detection):
    ///   - Kullanıcı konuştuğu sürece streaming devam eder.
    ///   - VAD_SILENCE_MS ms sessizlik algılanırsa stream otomatik durdurulur.
    ///   - Maksimum süre MAX_RECORD_SEC saniyedir.
    /// </summary>
    public class LiveAudioService : IDisposable
    {
        private WaveInEvent?           _waveIn;
        private WasapiLoopbackCapture? _loopbackCapture;
        private WaveOutEvent?          _waveOut;
        private BufferedWaveProvider?  _playBuffer;

        // ── Audio Queues for Microphone and Loopback (Requirement 1.6) ───────
        private readonly Queue<byte[]> _micQueue = new Queue<byte[]>();
        private readonly Queue<byte[]> _loopbackQueue = new Queue<byte[]>();
        private readonly object        _queueLock = new object();
        
        // ── Loopback Configuration (Requirement 1) ────────────────────────────
        public bool EnableLoopback { get; set; } = true;

        // ── Rolling Buffer for Wake Word Clipping Prevention ──────────────────
        private readonly Queue<byte[]> _mutedRollingBuffer = new Queue<byte[]>();
        private const int MaxRollingChunks = 15; // 15 chunks * 100ms = 1.5 seconds

        // ── Speaking State Management (Requirement 2, 5, 25) ──────────────────
        private readonly object _speakingLock = new object();
        private bool _isSpeaking = false;
        private DateTime _lastSpeakTime = DateTime.MinValue;

        public event Action<byte[]>? OnAudioInput;
        public event Action?         OnSilenceDetected;   // VAD: sessizlik algılandı
        public event Action?         OnVoiceDetected;      // VAD: ses algılandı (Requirement 3.4)

        public bool IsRecording { get; private set; }

        private bool _isMuted;
        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted && !value) // Susturulmuş moddan çıkılıyor (Uyandı)
                {
                    // Hafızadaki son 1.5 saniyeyi ana kuyruğa boşalt (İlk kelimeleri kurtar)
                    lock (_queueLock)
                    {
                        lock (_mutedRollingBuffer)
                        {
                            while (_mutedRollingBuffer.Count > 0)
                                _micQueue.Enqueue(_mutedRollingBuffer.Dequeue());
                        }
                    }
                }
                else if (!_isMuted && value) // Susturulmuş moda giriliyor
                {
                    lock (_mutedRollingBuffer) { _mutedRollingBuffer.Clear(); }
                }
                _isMuted = value;
            }
        }

        // ── VAD parametreleri ─────────────────────────────────────────────────
        private const double VadRmsThreshold = 300.0;  // Ses eşiği (RMS) - DEPRECATED, use dynamic threshold
        private const int    VadSilenceMs    = 1500;   // Bu kadar sessizlik → stream bitir
        private const int    MaxRecordSec    = 30;     // Maksimum kayıt süresi
        private DateTime     _lastVoiceTime  = DateTime.MinValue;
        private bool         _vadActive      = false;
        private CancellationTokenSource? _vadCts;

        // ── Dynamic Noise Baseline (Requirements 3, 4, 25) ────────────────────
        private const double InitialNoiseBaseline = 400.0;
        private const double ThresholdMultiplier  = 1.5;
        private const int    NoiseBufferSize      = 100;
        private double       _noiseBaseline       = InitialNoiseBaseline;
        private readonly List<double> _noiseSamples = new List<double>();
        private readonly object       _noiseLock    = new object();

        /// <summary>
        /// Sets the speaking state for feedback loop prevention.
        /// Thread-safe method called when Jarvis starts or stops speaking.
        /// Implements Requirements 2, 5, 25
        /// </summary>
        /// <param name="speaking">True when Jarvis starts speaking, false when stops</param>
        public void SetSpeakingState(bool speaking)
        {
            lock (_speakingLock)
            {
                _isSpeaking = speaking;
                
                if (!speaking)
                {
                    // Record stop timestamp for 800ms cooldown (Requirement 5.3, 5.5)
                    _lastSpeakTime = DateTime.UtcNow;
                    Logger.Information("Speaking stopped, 800ms cooldown started");
                }
                else
                {
                    Logger.Information("Speaking started, loopback audio will be discarded");
                }
            }
        }

        public LiveAudioService()
        {
            var playFormat = new WaveFormat(24000, 16, 1);
            _playBuffer = new BufferedWaveProvider(playFormat)
            {
                BufferDuration          = TimeSpan.FromSeconds(10),
                DiscardOnBufferOverflow = true
            };

            _waveOut = new WaveOutEvent();
            _waveOut.Init(_playBuffer);
            _waveOut.Play();

            Logger.Information("LiveAudioService playback initialized (24kHz)");
        }

        // ── Kayıt ─────────────────────────────────────────────────────────────

        public void StartRecording()
        {
            if (IsRecording) return;

            try
            {
                _vadCts?.Cancel();
                _vadCts = new CancellationTokenSource();

                var recordFormat = new WaveFormat(16000, 16, 1);
                _waveIn = new WaveInEvent
                {
                    WaveFormat         = recordFormat,
                    BufferMilliseconds = 100
                };

                _lastVoiceTime = DateTime.UtcNow;
                _vadActive     = true;

                // Requirement 4.4: Initialize noise baseline to 400.0
                lock (_noiseLock)
                {
                    _noiseBaseline = InitialNoiseBaseline;
                    _noiseSamples.Clear();
                }

                _waveIn.DataAvailable += OnData;
                _waveIn.StartRecording();
                IsRecording = true;

                Logger.Information("Started live microphone streaming (16kHz, VAD active)");

                // ── Initialize Loopback Capture (Requirement 1.1, 1.2, 1.4, 1.5) ──
                if (EnableLoopback)
                {
                    InitializeLoopbackCapture();
                }

                // VAD + MaxTime watchdog
                _ = Task.Run(() => VadWatchdog(_vadCts.Token));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start live microphone streaming");
            }
        }

        public void StopRecording()
        {
            if (!IsRecording) return;

            _vadActive = false;
            _vadCts?.Cancel();

            try
            {
                _waveIn?.StopRecording();
                _waveIn?.Dispose();
                _waveIn = null;
                
                // ── Stop Loopback Capture ─────────────────────────────────────
                StopLoopbackCapture();
                
                IsRecording = false;
                
                // Clear audio queues when stopping recording
                lock (_queueLock)
                {
                    _micQueue.Clear();
                    _loopbackQueue.Clear();
                }
                
                Logger.Information("Stopped live microphone streaming");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to stop live microphone streaming");
            }
        }

        // ── Queue Access Methods (for testing and mixing logic) ──────────────

        /// <summary>
        /// Gets the number of microphone audio chunks in the queue.
        /// </summary>
        public int GetMicQueueCount()
        {
            lock (_queueLock)
            {
                return _micQueue.Count;
            }
        }

        /// <summary>
        /// Gets the number of loopback audio chunks in the queue.
        /// </summary>
        public int GetLoopbackQueueCount()
        {
            lock (_queueLock)
            {
                return _loopbackQueue.Count;
            }
        }

        /// <summary>
        /// Gets the current dynamic noise baseline value.
        /// Used for testing and diagnostics.
        /// </summary>
        public double GetNoiseBaseline()
        {
            lock (_noiseLock)
            {
                return _noiseBaseline;
            }
        }

        /// <summary>
        /// Gets the number of noise samples currently in the rolling buffer.
        /// Used for testing and diagnostics.
        /// </summary>
        public int GetNoiseSampleCount()
        {
            lock (_noiseLock)
            {
                return _noiseSamples.Count;
            }
        }

        /// <summary>
        /// Dequeues a microphone audio chunk if available.
        /// </summary>
        public byte[]? DequeueMicChunk()
        {
            lock (_queueLock)
            {
                return _micQueue.Count > 0 ? _micQueue.Dequeue() : null;
            }
        }

        /// <summary>
        /// Dequeues a loopback audio chunk if available.
        /// </summary>
        public byte[]? DequeueLoopbackChunk()
        {
            lock (_queueLock)
            {
                return _loopbackQueue.Count > 0 ? _loopbackQueue.Dequeue() : null;
            }
        }

        // ── Audio data handler ────────────────────────────────────────────────

        private void OnData(object? sender, WaveInEventArgs e)
        {
            if (!IsRecording || e.BytesRecorded == 0) return;

            var buffer = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, buffer, e.BytesRecorded);

            if (IsMuted)
            {
                // Sessiz modda sesi 1.5 saniyelik hafızaya al (ilk kelimenin yutulmaması için)
                lock (_mutedRollingBuffer)
                {
                    _mutedRollingBuffer.Enqueue(buffer);
                    if (_mutedRollingBuffer.Count > MaxRollingChunks)
                        _mutedRollingBuffer.Dequeue();
                }

                // Gürültü eşiğini sessizlikte de güncellemeye devam et
                double mutedRms = CalcRms(buffer, e.BytesRecorded);
                UpdateNoiseBaseline(mutedRms);
                return;
            }

            // VAD — RMS hesapla
            double rms = CalcRms(buffer, e.BytesRecorded);
            
            // Calculate dynamic threshold (Requirement 3.5, 3.6, 4.5)
            double dynamicThreshold = Math.Max(InitialNoiseBaseline, _noiseBaseline * ThresholdMultiplier);
            
            // Check if voice detected (Requirement 3.3)
            bool voiceDetected = rms > dynamicThreshold;
            
            if (voiceDetected)
            {
                // Update last voice time immediately (Requirement 3.3)
                _lastVoiceTime = DateTime.UtcNow;
                
                // Trigger UI head rotation immediately with zero delay (Requirement 3.4)
                OnVoiceDetected?.Invoke();
            }
            else
            {
                // Update noise baseline during silence (Requirement 4.3)
                UpdateNoiseBaseline(rms);
            }

            // Mikrofon ses chunk'ını kuyruğa ekle (Requirement 1.6)
            lock (_queueLock)
            {
                _micQueue.Enqueue(buffer);
            }

            // Mix and send audio (Task 9.2, 9.3 - Requirements 2, 5, 25)
            MixAndSendAudio();
        }

        // ── VAD watchdog ──────────────────────────────────────────────────────

        private async Task VadWatchdog(CancellationToken ct)
        {
            var startTime = DateTime.UtcNow;

            while (!ct.IsCancellationRequested && IsRecording)
            {
                await Task.Delay(100, ct).ContinueWith(_ => { }); // suppress exception

                if (ct.IsCancellationRequested) break;

                var now         = DateTime.UtcNow;
                var silenceMs   = (now - _lastVoiceTime).TotalMilliseconds;
                var totalSec    = (now - startTime).TotalSeconds;

                // Maksimum süre aşıldı
                if (totalSec >= MaxRecordSec)
                {
                    Logger.Information($"VAD: max record time ({MaxRecordSec}s) reached");
                    FireSilence();
                    // Döngüyü kırmak yerine zamanlayıcıları sıfırla ki arka planda çalışmaya devam etsin
                    startTime = DateTime.UtcNow;
                    _lastVoiceTime = DateTime.MinValue;
                }

                // Sessizlik eşiği aşıldı
                if (_vadActive && silenceMs >= VadSilenceMs && _lastVoiceTime != DateTime.MinValue)
                {
                    Logger.Information($"VAD: silence detected ({silenceMs:F0}ms)");
                    FireSilence();
                    // Döngüyü kırmak yerine _lastVoiceTime sıfırla ki bir sonraki sesi beklesin
                    _lastVoiceTime = DateTime.MinValue;
                }
            }
        }

        private void FireSilence()
        {
            OnSilenceDetected?.Invoke();
        }

        // ── Oynatma ───────────────────────────────────────────────────────────

        public void PlayAudio(byte[] pcmData)
        {
            try { _playBuffer?.AddSamples(pcmData, 0, pcmData.Length); }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to queue audio: {ex.Message}");
            }
        }

        // ── Yardımcı ─────────────────────────────────────────────────────────

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

        /// <summary>
        /// Updates the dynamic noise baseline with a new RMS sample during silence.
        /// Implements Requirements 4.1, 4.2, 4.3, 4.4, 4.6
        /// </summary>
        /// <param name="rms">RMS value from current audio chunk</param>
        private void UpdateNoiseBaseline(double rms)
        {
            lock (_noiseLock)
            {
                // Requirement 4.1, 4.3: Maintain rolling buffer of 100 RMS samples during silence
                _noiseSamples.Add(rms);
                
                // Requirement 4.6: Discard oldest sample when buffer exceeds 100
                if (_noiseSamples.Count > NoiseBufferSize)
                {
                    _noiseSamples.RemoveAt(0);
                }
                
                // Requirement 4.2: Calculate baseline as average of buffer
                if (_noiseSamples.Count > 0)
                {
                    _noiseBaseline = _noiseSamples.Average();
                }
            }
        }

        /// <summary>
        /// Mixes microphone and loopback audio and sends to Gemini.
        /// Implements Requirements 2.1, 2.2, 2.3, 2.4, 5.4, 5.5, 5.6, 25.2
        /// Task 9.2: Audio Mixing Logic
        /// Task 9.3: Integration into Pipeline
        /// </summary>
        private void MixAndSendAudio()
        {
            byte[]? micBytes = null;
            byte[]? loopbackBytes = null;
            
            // Dequeue microphone chunk
            lock (_queueLock)
            {
                if (_micQueue.Count > 0)
                {
                    micBytes = _micQueue.Dequeue();
                }
                
                // Try to dequeue loopback chunk if available
                if (_loopbackQueue.Count > 0)
                {
                    loopbackBytes = _loopbackQueue.Dequeue();
                }
            }
            
            // If no microphone data, nothing to send
            if (micBytes == null) return;
            
            // Check speaking state and cooldown (Requirement 5.1, 5.3, 5.5)
            bool jarvisSpeaking;
            DateTime lastSpeak;
            
            lock (_speakingLock)
            {
                jarvisSpeaking = _isSpeaking;
                lastSpeak = _lastSpeakTime;
            }
            
            // Requirement 5.5: 800ms cooldown after speaking stops
            // Requirement 5.6: Allow loopback only when not speaking AND cooldown elapsed
            double secondsSinceSpeak = (DateTime.UtcNow - lastSpeak).TotalMilliseconds / 1000.0;
            bool allowLoopback = !jarvisSpeaking && secondsSinceSpeak > 0.8;
            
            // Requirement 5.4: Discard all queued loopback when speaking or in cooldown
            if (!allowLoopback)
            {
                lock (_queueLock)
                {
                    _loopbackQueue.Clear();
                }
                loopbackBytes = null;
            }
            
            // Requirement 2.1, 2.4: Combine microphone and loopback audio chunks
            byte[] outputBytes;
            
            if (loopbackBytes != null && loopbackBytes.Length > 0)
            {
                // Mix the two audio streams (Requirement 2.1)
                outputBytes = MixAudioChunks(micBytes, loopbackBytes);
            }
            else
            {
                // Send microphone only
                outputBytes = micBytes;
            }
            
            // Requirement 2.5, 25.2: Send mixed audio chunks to Gemini
            OnAudioInput?.Invoke(outputBytes);
        }

        /// <summary>
        /// Mixes two PCM audio chunks by adding their samples together.
        /// Implements Requirement 2.1, 2.4 (using integer operations instead of numpy)
        /// </summary>
        /// <param name="micBytes">Microphone audio chunk (int16 PCM)</param>
        /// <param name="loopbackBytes">Loopback audio chunk (int16 PCM)</param>
        /// <returns>Mixed audio chunk (int16 PCM)</returns>
        private byte[] MixAudioChunks(byte[] micBytes, byte[] loopbackBytes)
        {
            // Determine the minimum length to mix
            int micSamples = micBytes.Length / 2;  // 2 bytes per int16 sample
            int loopSamples = loopbackBytes.Length / 2;
            int mixLength = Math.Min(micSamples, loopSamples);
            
            // Allocate output buffer
            byte[] output = new byte[micBytes.Length];
            
            // Mix the overlapping portion
            for (int i = 0; i < mixLength; i++)
            {
                int byteIndex = i * 2;
                
                // Read int16 samples from both sources
                short micSample = BitConverter.ToInt16(micBytes, byteIndex);
                short loopSample = BitConverter.ToInt16(loopbackBytes, byteIndex);
                
                // Mix by adding (use int32 to prevent overflow)
                int mixed = (int)micSample + (int)loopSample;
                
                // Clamp to int16 range
                if (mixed > short.MaxValue) mixed = short.MaxValue;
                if (mixed < short.MinValue) mixed = short.MinValue;
                
                // Write back to output
                byte[] mixedBytes = BitConverter.GetBytes((short)mixed);
                output[byteIndex] = mixedBytes[0];
                output[byteIndex + 1] = mixedBytes[1];
            }
            
            // Copy any remaining microphone samples if mic is longer
            if (micBytes.Length > mixLength * 2)
            {
                Array.Copy(micBytes, mixLength * 2, output, mixLength * 2, micBytes.Length - mixLength * 2);
            }
            
            return output;
        }

        // ── Loopback Capture Methods (Requirement 1: System Audio Recording) ──

        /// <summary>
        /// Initializes WasapiLoopbackCapture for system audio recording.
        /// Implements Requirements 1.1, 1.2, 1.3, 1.4, 1.5
        /// </summary>
        private void InitializeLoopbackCapture()
        {
            try
            {
                // Requirement 1.1: Detect and open loopback device
                _loopbackCapture = new WasapiLoopbackCapture();

                // Requirement 1.2: Configure for 16kHz mono output
                // Note: WasapiLoopbackCapture captures at device native format (typically 48kHz stereo)
                // We'll resample and downmix in the data handler
                _loopbackCapture.DataAvailable += OnLoopbackData;
                _loopbackCapture.RecordingStopped += OnLoopbackStopped;

                _loopbackCapture.StartRecording();

                Logger.Information($"Loopback capture initialized: {_loopbackCapture.WaveFormat.SampleRate}Hz, {_loopbackCapture.WaveFormat.Channels}ch");
            }
            catch (Exception ex)
            {
                // Requirement 1.3: Handle device unavailability gracefully with warning log
                Logger.Warning($"Loopback device not available, continuing with microphone-only mode: {ex.Message}");
                _loopbackCapture = null;
            }
        }

        /// <summary>
        /// Handles loopback audio data capture.
        /// Implements Requirements 1.2, 1.4, 1.5, 1.6
        /// </summary>
        private void OnLoopbackData(object? sender, WaveInEventArgs e)
        {
            if (!IsRecording || _loopbackCapture == null || e.BytesRecorded == 0) return;

            try
            {
                var sourceFormat = _loopbackCapture.WaveFormat;
                
                // Requirement 1.4: Downmix stereo loopback audio to mono
                // Requirement 1.5: Convert float32 loopback samples to int16 PCM format
                // Requirement 1.2: Resample to 16kHz
                byte[] processedData = ProcessLoopbackAudio(
                    e.Buffer, 
                    e.BytesRecorded, 
                    sourceFormat
                );

                if (processedData.Length > 0)
                {
                    // Requirement 1.6: Maintain separate queues for loopback
                    lock (_queueLock)
                    {
                        _loopbackQueue.Enqueue(processedData);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error processing loopback audio: {ex.Message}");
            }
        }

        /// <summary>
        /// Processes loopback audio: resamples to 16kHz, downmixes to mono, converts to int16 PCM.
        /// Implements Requirements 1.2, 1.4, 1.5
        /// </summary>
        private byte[] ProcessLoopbackAudio(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
        {
            try
            {
                // Create a provider from the captured data
                var sourceProvider = new BufferedWaveProvider(sourceFormat)
                {
                    BufferDuration = TimeSpan.FromSeconds(10),
                    DiscardOnBufferOverflow = true
                };
                sourceProvider.AddSamples(buffer, 0, bytesRecorded);

                // Convert to sample provider
                ISampleProvider sampleProvider = sourceProvider.ToSampleProvider();

                // Downmix stereo to mono if necessary (Requirement 1.4)
                if (sourceFormat.Channels > 1)
                {
                    sampleProvider = sampleProvider.ToMono();
                }

                // Resample to 16kHz if necessary (Requirement 1.2)
                if (sourceFormat.SampleRate != 16000)
                {
                    var resampler = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(sampleProvider, 16000);
                    sampleProvider = resampler;
                }

                // Read samples (float32) and convert to int16 PCM (Requirement 1.5)
                var floatBuffer = new float[bytesRecorded / sourceFormat.BlockAlign * 2]; // Allocate enough
                int samplesRead = sampleProvider.Read(floatBuffer, 0, floatBuffer.Length);
                
                if (samplesRead > 0)
                {
                    // Convert float32 samples to int16
                    var int16Buffer = new byte[samplesRead * 2]; // 2 bytes per int16 sample
                    
                    for (int i = 0; i < samplesRead; i++)
                    {
                        // Clamp to [-1.0, 1.0] and convert to int16 range
                        float sample = Math.Max(-1.0f, Math.Min(1.0f, floatBuffer[i]));
                        short int16Sample = (short)(sample * short.MaxValue);
                        BitConverter.GetBytes(int16Sample).CopyTo(int16Buffer, i * 2);
                    }
                    
                    return int16Buffer;
                }

                return Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error in loopback audio processing pipeline: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Handles loopback recording stopped event.
        /// </summary>
        private void OnLoopbackStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Warning($"Loopback capture stopped with error: {e.Exception.Message}");
            }
        }

        /// <summary>
        /// Stops and disposes the loopback capture.
        /// </summary>
        private void StopLoopbackCapture()
        {
            try
            {
                if (_loopbackCapture != null)
                {
                    _loopbackCapture.DataAvailable -= OnLoopbackData;
                    _loopbackCapture.RecordingStopped -= OnLoopbackStopped;
                    _loopbackCapture.StopRecording();
                    _loopbackCapture.Dispose();
                    _loopbackCapture = null;
                    Logger.Information("Stopped loopback capture");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error stopping loopback capture: {ex.Message}");
            }
        }

        public void Dispose()
        {
            StopRecording();
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _vadCts?.Dispose();
        }
    }
}
