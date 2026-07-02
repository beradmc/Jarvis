using System;
using System.Diagnostics;
using NAudio.Wave;

namespace JarvisCSharp.Audio;

/// <summary>
/// Wake word detection for "Hey Jarvis".
///
/// Uses NAudio for microphone capture with a two-stage approach:
///   1. Energy-based voice activity detection (VAD) — detects when the user is speaking
///   2. Keyword spotting — when speech is detected, runs a simple frequency pattern
///      match against the "Jarvis" phoneme signature
///
/// This is intentionally lightweight (no neural model) to run continuously in the
/// background with near-zero CPU impact. For production-grade accuracy, this would
/// be replaced with a proper wake word model (e.g. openWakeWord, Porcupine, or
/// Windows.Media.SpeechRecognition with a constrained grammar).
///
/// Fallback: if the microphone is unavailable or permission is denied, the wake
/// word service silently disables itself and the user can still summon Jarvis via
/// the Win+J hotkey.
/// </summary>
public sealed class WakeWordService : IDisposable
{
    private WaveInEvent? _waveIn;
    private bool _running;
    private bool _disposed;

    // VAD state
    private const int SampleRate = 16000;
    private const int BufferMs = 30;
    private const int BufferSamples = SampleRate * BufferMs / 1000;
    private readonly float[] _buffer = new float[BufferSamples];
    private int _bufferIndex;

    // Energy threshold for voice activity (tuned for typical desktop mic)
    private const double VoiceEnergyThreshold = 0.015;
    private const double SilenceThreshold = 0.005;

    // State machine
    private enum State { Idle, Listening, Speaking }
    private State _state = State.Idle;
    private DateTime _lastVoiceTime;
    private readonly TimeSpan _speechTimeout = TimeSpan.FromSeconds(2);

    // Keyword detection — simple energy envelope matching
    // "Jarvis" has a characteristic 2-syllable pattern: JAR-vis
    // We look for a burst of energy followed by a dip, then another burst
    private readonly List<double> _energyHistory = new();
    private const int EnergyHistorySize = 50; // ~1.5 seconds of history

    /// <summary>Fired when the wake word "Hey Jarvis" is detected.</summary>
    public event Action? WakeWordDetected;

    /// <summary>Whether the wake word listener is currently active.</summary>
    public bool IsActive => _running;

    /// <summary>
    /// Start listening for the wake word. Requires microphone access.
    /// Silently fails if the microphone is unavailable.
    /// </summary>
    public void Start()
    {
        if (_running || _disposed) return;

        try
        {
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(SampleRate, 16, 1),
                BufferMilliseconds = BufferMs,
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            _running = true;
            Debug.WriteLine("[WakeWord] Listening for 'Hey Jarvis'...");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WakeWord] Failed to start: {ex.Message}");
            _running = false;
        }
    }

    /// <summary>Stop listening for the wake word.</summary>
    public void Stop()
    {
        if (!_running) return;

        try
        {
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
        }
        catch { /* non-critical */ }

        _waveIn = null;
        _running = false;
        _state = State.Idle;
        _energyHistory.Clear();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running) return;

        // Convert 16-bit samples to float
        int samples = e.BytesRecorded / 2;
        for (int i = 0; i < samples && _bufferIndex < _buffer.Length; i++)
        {
            short sample = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
            _buffer[_bufferIndex++] = sample / 32768f;
        }

        if (_bufferIndex >= _buffer.Length)
        {
            ProcessBuffer();
            _bufferIndex = 0;
        }
    }

    private void ProcessBuffer()
    {
        // Calculate RMS energy
        double sum = 0;
        for (int i = 0; i < _buffer.Length; i++)
        {
            sum += _buffer[i] * _buffer[i];
        }
        double rms = Math.Sqrt(sum / _buffer.Length);

        // Add to history
        _energyHistory.Add(rms);
        if (_energyHistory.Count > EnergyHistorySize)
            _energyHistory.RemoveAt(0);

        // State machine for wake word detection
        switch (_state)
        {
            case State.Idle:
                // Wait for voice activity
                if (rms > VoiceEnergyThreshold)
                {
                    _state = State.Listening;
                    _lastVoiceTime = DateTime.Now;
                }
                break;

            case State.Listening:
                // Track speech and look for the keyword pattern
                if (rms > VoiceEnergyThreshold)
                {
                    _lastVoiceTime = DateTime.Now;
                }

                // Check if we have enough history to try keyword detection
                if (_energyHistory.Count >= EnergyHistorySize && _state == State.Listening)
                {
                    if (TryDetectKeyword())
                    {
                        _state = State.Idle;
                        _energyHistory.Clear();
                        WakeWordDetected?.Invoke();
                        return;
                    }
                }

                // Timeout — go back to idle if no speech for a while
                if (DateTime.Now - _lastVoiceTime > _speechTimeout)
                {
                    _state = State.Idle;
                    _energyHistory.Clear();
                }
                break;
        }
    }

    /// <summary>
    /// Simple keyword detection based on the energy envelope.
    /// "Hey Jarvis" has a characteristic pattern:
    ///   - Brief pause → "Hey" (energy burst ~200ms)
    ///   - Brief dip → "Jar" (rising energy ~200ms)
    ///   - Peak → "vis" (energy burst ~150ms)
    ///
    /// This is intentionally approximate. It will have false positives but
    /// the user can dismiss the orb easily. A proper model would use
    /// spectrogram matching or a small neural network.
    /// </summary>
    private bool TryDetectKeyword()
    {
        if (_energyHistory.Count < 20) return false;

        // Find the energy profile over the last ~1 second
        var recent = _energyHistory.TakeLast(33).ToArray(); // ~1 second

        // Look for a pattern: silence → burst → dip → burst (2-syllable word)
        double avgEnergy = recent.Average();
        if (avgEnergy < VoiceEnergyThreshold * 0.5) return false;

        // Check for the characteristic rise-dip-rise pattern
        int midPoint = recent.Length / 2;
        double firstHalf = recent[..midPoint].Average();
        double secondHalf = recent[midPoint..].Average();
        double minMid = recent.Skip(midPoint - 5).Take(10).Min();

        // "Hey Jarvis" pattern: energy in first half, dip in middle, energy in second half
        bool hasFirstBurst = firstHalf > VoiceEnergyThreshold;
        bool hasDip = minMid < firstHalf * 0.5;
        bool hasSecondBurst = secondHalf > VoiceEnergyThreshold;

        // Only trigger if we haven't triggered recently (debounce 5 seconds)
        if (hasFirstBurst && hasDip && hasSecondBurst)
        {
            if (DateTime.Now - _lastDetection > TimeSpan.FromSeconds(5))
            {
                _lastDetection = DateTime.Now;
                Debug.WriteLine("[WakeWord] Detected 'Hey Jarvis'!");
                return true;
            }
        }

        return false;
    }

    private DateTime _lastDetection = DateTime.MinValue;

    public void Dispose()
    {
        Stop();
        _disposed = true;
    }
}
