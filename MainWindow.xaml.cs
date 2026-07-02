using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.ComponentModel;
using JarvisCSharp.Core;
using JarvisCSharp.Utils;
using JarvisCSharp.Config;
using JarvisCSharp.Services;
using JarvisCSharp.Audio;
using JarvisCSharp.AI;
using JarvisCSharp.Models;
using JarvisCSharp.Actions;
using JarvisCSharp.Utils;

namespace JarvisCSharp;

/// <summary>
/// Jarvis operating modes for power management and user interaction control.
/// </summary>
public enum JarvisMode
{
    /// <summary>
    /// PASSIVE: Sleeping/Standby mode. Only wake-word/clap detection active.
    /// Gemini session closed. User must say "Jarvis" or clap to activate.
    /// Hologram: Blue/Calm
    /// </summary>
    PASSIVE,

    /// <summary>
    /// ACTIVE: Fully active and listening mode. Gemini Live Session open.
    /// Continuously listening, responding, executing tools.
    /// User can say "Jarvis kapan" or "Jarvis sleep" to enter PASSIVE mode.
    /// Hologram: Green/Active
    /// </summary>
    ACTIVE,

    /// <summary>
    /// MUTED: Listening but not speaking mode. Gemini session open.
    /// Hears commands, executes tools, but TTS is disabled.
    /// User can say "Jarvis konuş" to return to ACTIVE mode.
    /// Hologram: Orange/Quiet
    /// </summary>
    MUTED
}

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new DispatcherTimer
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private DispatcherTimer? _healthPanelTimer;
    private bool _isInitialized = false;
    private bool _isMuted       = false;
    private bool _isPaused      = false;

    // 3-Mode System State
    private JarvisMode _currentMode = JarvisMode.PASSIVE;

    // Wake word cooldown tracking (Requirement 10.1, 10.6, 19.1)
    private DateTime _wakeWordCooldownUntil = DateTime.MinValue;

    private UI.HeadMeshRenderer? _headRenderer;
    private System.Windows.Media.Imaging.WriteableBitmap? _headBitmap;
    private double _headRotY     = 0.0;
    private double _headActivity = 1.0;

    // Tool confirmation system (Requirement 21, 30)
    private PendingConfirmation? _pendingConfirmation;

    private readonly SystemInfoService _systemInfoService;
    private readonly GeminiService     _geminiService;
    private readonly WakeupListener    _wakeupListener;
    private readonly TtsService        _ttsService;
    private readonly LiveAudioService  _liveAudioService;
    private readonly ActionManager     _actionManager;
    private readonly LiveVisionService _liveVisionService;

    // Global keyboard hook
    private LowLevelKeyboardHook? _keyboardHook;

    public MainWindow(SystemInfoService systemInfoService, GeminiService geminiService,
                      WakeupListener wakeupListener, TtsService ttsService,
                      LiveAudioService liveAudioService, ActionManager actionManager,
                      LiveVisionService liveVisionService)
    {
        _systemInfoService = systemInfoService;
        _geminiService     = geminiService;
        _wakeupListener    = wakeupListener;
        _ttsService        = ttsService;
        _liveAudioService  = liveAudioService;
        _actionManager     = actionManager;
        _liveVisionService = liveVisionService;

        _actionManager.OnModeChangeRequested += async (mode) =>
        {
            await Dispatcher.InvokeAsync(async () => {
                if (mode == "muted") await EnterMutedMode();
                else if (mode == "passive") await EnterPassiveMode();
                else if (mode == "active") await EnterActiveMode(_currentMode);
            });
        };

        InitializeComponent();
        InitializeLogger();
        InitializeTimer();
        InitializeUI();
    }

    private void InitializeLogger()
    {
        Logger.Information("JARVIS C# Edition starting...");
        Logger.Information("Berat Demirci tarafından yapılmıştır — @beratdemirci");
    }

    private void InitializeTimer()
    {
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void InitializeUI()
    {
        // ── API key kontrolü ──────────────────────────────────────────────────
        if (!AppConfig.HasGeminiApiKey())
        {
            Logger.Warning("Gemini API key not found in config");
            StatusText.Text       = "NO API KEY";
            StatusText.Foreground = FindResource("RedBrush") as System.Windows.Media.Brush;
        }
        else
        {
            Logger.Information("Gemini API key loaded");
            StatusText.Text       = "READY";
            StatusText.Foreground = FindResource("GreenBrush") as System.Windows.Media.Brush;
        }

        // ── 3D Head Mesh ──────────────────────────────────────────────────────
        _headRenderer = new UI.HeadMeshRenderer();
        _headBitmap   = new System.Windows.Media.Imaging.WriteableBitmap(
            400, 400, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
        HologramImage.Source = _headBitmap;
        System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_Rendering;

        // ── TTS olayları — hologram konuşmaya tepki verir ────────────────────
        _ttsService.SpeakingStarted  += (s, e) => Dispatcher.Invoke(() => {
            _headActivity = 2.5;
            SetStatus("SPEAKING", "OrangeBrush");
        });
        _ttsService.SpeakingFinished += (s, e) => Dispatcher.Invoke(() => {
            _headActivity = 1.0;
            SetStatus("READY", "GreenBrush");
        });

        // ── Mute butonu ───────────────────────────────────────────────────────
        MuteButton.Click += (s, e) =>
        {
            _isMuted               = !_isMuted;
            _liveAudioService.IsMuted = _isMuted;
            MuteButton.Content     = _isMuted ? "🔇 Unmute" : "🔊 Mute";
            WriteLog(_isMuted ? "🔇 Mikrofon kapatıldı." : "🔊 Mikrofon açıldı.");
            SetStatus(_isMuted ? "MUTED" : "READY", _isMuted ? "RedBrush" : "GreenBrush");
        };

        // ── Pause butonu ──────────────────────────────────────────────────────
        PauseButton.Click += (s, e) =>
        {
            _isPaused          = !_isPaused;
            PauseButton.Content = _isPaused ? "▶ Resume" : "⏸ Pause";
            WriteLog(_isPaused ? "⏸ JARVIS duraklatıldı." : "▶ JARVIS devam ediyor.");

            if (_isPaused)
            {
                _wakeupListener.PauseClapDetector();
                if (_liveAudioService.IsRecording) StopListening();
                SetStatus("PAUSED", "OrangeBrush");
            }
            else
            {
                _wakeupListener.ResumeClapDetector();
                SetStatus("READY", "GreenBrush");
            }
        };

        // ── Stop butonu ───────────────────────────────────────────────────────
        StopButton.Click += (s, e) =>
        {
            WriteLog("⏹ Durduruldu.");
            _ttsService.StopSpeaking();
            StopListening();
        };

        // ── Ayarlar butonu ────────────────────────────────────────────────────
        // SettingsButton_Click is mapped directly in XAML so we define a separate method


        // ── Metin girişi — Enter ile Gemini'ye gönder ─────────────────────────
        CommandInput.KeyDown += async (s, e) =>
        {
            if (e.Key != Key.Enter) return;
            if (_isPaused)          return;

            var text = CommandInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;
            CommandInput.Text = "";
            WriteLog($"> {text}");

            // Check for mode change commands first
            if (await HandleModeChangeCommand(text))
            {
                return; // Mode changed, don't send to Gemini
            }

            // Check for pending confirmation (Requirement 21, 30)
            if (_pendingConfirmation != null && IsConfirmationText(text))
            {
                await ExecuteConfirmedTool(_pendingConfirmation);
                return;
            }

            // PASSIVE mode: ignore text input (user must wake up first)
            if (_currentMode == JarvisMode.PASSIVE)
            {
                WriteLog("💤 JARVIS uyuyor. Önce 'Jarvis' de veya el çırp.");
                return;
            }

            // Check if session is ready before sending message
            if (!_geminiService.IsSessionOpen)
            {
                if (_currentMode != JarvisMode.PASSIVE)
                {
                    WriteLog("🔄 Bağlantı koptu, yeniden bağlanılıyor...");
                    try
                    {
                        await _geminiService.StartLiveSessionAsync();
                        _liveVisionService.StartStreaming();
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"❌ Yeniden bağlanılamadı: {ex.Message}");
                        return;
                    }
                }
                else
                {
                    WriteLog("⏳ Session henüz hazır değil, lütfen biraz bekleyin...");
                    return;
                }
            }

            SetStatus("THINKING", "OrangeBrush");
            try
            {
                await _geminiService.GenerateTextAsync(text);
                SetStatus(_currentMode == JarvisMode.ACTIVE ? "READY" : "MUTED", 
                         _currentMode == JarvisMode.ACTIVE ? "GreenBrush" : "OrangeBrush");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Gemini error");
                WriteLog($"[HATA] {ex.Message}");
                SetStatus("ERROR", "RedBrush");
            }
        };

        // Gemini Live Audio olayları ────────────────────────────────────────

        // Tool confirmation needed (Requirement 20, 21, 30)
        _geminiService.OnConfirmationNeeded += (toolName, jsonArgs, reason) =>
        {
            Dispatcher.Invoke(() => {
                _pendingConfirmation = new PendingConfirmation
                {
                    ToolName = toolName,
                    JsonArgs = jsonArgs,
                    Reason = reason,
                    ExpiresAt = DateTime.Now.AddSeconds(45)
                };

                WriteLog($"⚠️ ONAY GEREKLİ: {reason}");
                WriteLog($"   Devam etmek için 45 saniye içinde 'onayla', 'evet', veya 'tamam' deyin.");
                SetStatus("WAITING CONFIRM", "OrangeBrush");
            });
        };

        // Tool tamamlandı → sağlık panelini güncelle, success sound çal
        _geminiService.OnToolCompleted += (toolName, result) =>
        {
            Dispatcher.Invoke(() => {
                if (toolName == "get_health_data")
                {
                    UpdateHealthPanel(result);
                }
                
                // Task 19.1 & 19.2: Tool Success Sound Effects
                // Implements Requirement 24 - Tool Success Sound Effects
                if (ShouldPlaySuccessSound(toolName, result))
                {
                    PlaySuccessSound();
                }
            });
        };

        // Ses verisi geldi → oynat (MUTED değilse), hologram SPEAKING state'e gir
        _geminiService.OnAudioReceived += (pcmData) =>
        {
            // MUTED mode: Don't play TTS audio
            if (_currentMode != JarvisMode.MUTED)
            {
                _liveAudioService.PlayAudio(pcmData);
                Dispatcher.Invoke(() => {
                    _headActivity = 2.5;
                    SetStatus("SPEAKING", "OrangeBrush");
                });
            }
            else
            {
                // In MUTED mode, just animate hologram briefly without sound
                Dispatcher.Invoke(() => {
                    _headActivity = 1.3; // Subtle animation
                });
            }
        };

        // Metin geldi (transcription veya text response)
        _geminiService.OnTextReceived += async (text) =>
        {
            await Dispatcher.InvokeAsync(async () => {
                // Check for mode change commands in transcription
                if (await HandleModeChangeCommand(text))
                {
                    return; // Mode changed, don't display response
                }

                if (text.StartsWith("🔧") || text.StartsWith("✅"))
                {
                    // Tool çalıştırma mesajları — küçük göster
                    WriteLog($"  {text}");
                }
                else if (text.StartsWith("CONFIRM:"))
                {
                    // Confirmation message handled by OnConfirmationNeeded event
                    // Just display it
                    var reason = text.Substring("CONFIRM:".Length).Trim();
                    WriteLog($"⚠️ ONAY GEREKLİ: {reason}");
                }
                else
                {
                    var clean = StripToolCalls(text);
                    if (!string.IsNullOrWhiteSpace(clean))
                        WriteLog($"🤖 JARVIS: {clean}");
                    _headActivity = 1.0;
                    
                    // Set status based on mode
                    if (_currentMode == JarvisMode.ACTIVE)
                        SetStatus("READY", "GreenBrush");
                    else if (_currentMode == JarvisMode.MUTED)
                        SetStatus("MUTED", "OrangeBrush");
                }
            });
        };

        // Mikrofon verisi → Gemini'ye gönder
        _liveAudioService.OnAudioInput += async (pcmData) =>
        {
            if (!_isPaused && _geminiService.IsSessionOpen)
                await _geminiService.SendAudioAsync(pcmData);
        };

        // VAD: sessizlik algılandı → Gemini'ye akış sonu sinyali gönder (mikrofon açık kalır)
        _liveAudioService.OnSilenceDetected += async () =>
        {
            await _geminiService.SendAudioStreamEndAsync();
            // ACTIVE modda kesintisiz dinleme için mikrofon kapatılmıyor
            Dispatcher.Invoke(() => {
                SetStatus("READY", "GreenBrush");
            });
        };

        // VAD: ses algılandı → UI head rotation with zero delay (Requirement 3.4)
        _liveAudioService.OnVoiceDetected += () =>
        {
            Dispatcher.Invoke(() => {
                _headActivity = 2.0; // Trigger immediate head rotation
            });
        };

        // ── Gemini Live Session - PASSIVE mode'da kapalı başlar ──────────────
        // Session will be started when entering ACTIVE mode via wake-up
        WriteLog("💤 JARVIS PASSIVE modda başlatıldı. Uyandırmak için 'Jarvis' de veya el çırp.");

        // ── Wake word → Dinlemeye başla ───────────────────────────────────────
        _wakeupListener.WakeWordDetected += async (s, e) =>
        {
            if (_isPaused) return;
            if (_ttsService.IsSpeaking) return;

            await Dispatcher.InvokeAsync(async () => {
                await HandleWakeUp();

                // Only start recording if in ACTIVE mode
                if (_currentMode == JarvisMode.ACTIVE && !_liveAudioService.IsRecording)
                {
                    _wakeupListener.PauseClapDetector();
                    _liveAudioService.StartRecording();
                }
            });
        };

        _wakeupListener.Start();

        _isInitialized = true;
        UpdateDateTime();
        UpdateSystemInfo();
        
        // Setup Global Keyboard Hook
        _keyboardHook = new LowLevelKeyboardHook();
        _keyboardHook.SummonPressed += async () =>
        {
            await Dispatcher.InvokeAsync(async () => {
                if (this.WindowState == WindowState.Minimized)
                    this.WindowState = WindowState.Normal;
                this.Activate();
                this.Topmost = true;
                this.Topmost = false;
                
                if (_currentMode != JarvisMode.ACTIVE)
                {
                    await HandleWakeUp();
                    if (!_liveAudioService.IsRecording)
                    {
                        _wakeupListener.PauseClapDetector();
                        _liveAudioService.StartRecording();
                    }
                }
            });
        };
        _keyboardHook.EscapePressed += async () =>
        {
            await Dispatcher.InvokeAsync(async () => {
                if (_currentMode == JarvisMode.ACTIVE)
                    await EnterPassiveMode();
                this.WindowState = WindowState.Minimized;
            });
        };
        _keyboardHook.Install();
    }

    // ── Dinlemeyi durdur (ortak metot) ───────────────────────────────────────

    private void StopListening()
    {
        if (_liveAudioService.IsRecording)
            _liveAudioService.StopRecording();
    }

    // ── Timer / Rendering ─────────────────────────────────────────────────────

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _keyboardHook?.Dispose();
            _liveVisionService.StopStreaming();
            _wakeupListener.Stop();
        }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_isInitialized)
        {
            UpdateDateTime();
            UpdateSystemInfo();
        }
    }

    private void CompositionTarget_Rendering(object? sender, EventArgs e)
    {
        if (_headBitmap == null || _headRenderer == null) return;

        bool speaking = _ttsService.IsSpeaking || _headActivity > 1.5;
        _headRotY -= speaking ? 0.06 : 0.02;
        if (_headRotY < -Math.PI * 2) _headRotY += Math.PI * 2;

        // Renk mode'a göre ayarlanıyor
        byte r, g, b;

        if (speaking)
        {
            // Konuşurken turuncu/beyaz (tüm modlarda aynı)
            r = 80;
            g = 200;
            b = 255;
        }
        else
        {
            // Mode'a göre idle renkleri
            switch (_currentMode)
            {
                case JarvisMode.PASSIVE:
                    // Mavi/Sakin - Uyku modu
                    r = 0;
                    g = 100;
                    b = 255;
                    break;

                case JarvisMode.ACTIVE:
                    // Yeşil/Aktif - Hazır
                    r = 0;
                    g = 200;
                    b = 180;
                    break;

                case JarvisMode.MUTED:
                    // Turuncu/Susturulmuş
                    r = 255;
                    g = 150;
                    b = 0;
                    break;

                default:
                    r = 0;
                    g = 160;
                    b = 255;
                    break;
            }
        }

        // Hologram activity yavaşça idle'a dönsün
        if (_headActivity > 1.0)
            _headActivity = Math.Max(1.0, _headActivity - 0.02);

        _headRenderer.Render(_headBitmap, 0.15, _headRotY, 190.0, 200.0, 200.0, _headActivity, r, g, b);
    }

    private void UpdateDateTime()
    {
        TimeText.Text = DateTimeHelper.GetCurrentTurkishTime();
        DateText.Text = DateTimeHelper.GetCurrentTurkishDate();
    }

    private void UpdateSystemInfo()
    {
        CpuProgressBar.Value     = _systemInfoService.GetCpuUsage();
        RamProgressBar.Value     = _systemInfoService.GetRamUsage();
        BatteryProgressBar.Value = _systemInfoService.GetBatteryLevel();
    }

    // ── UI yardımcı metodlar ──────────────────────────────────────────────────

    public void WriteLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogText.Text += Environment.NewLine + message;
            var scrollViewer = LogText.Parent as ScrollViewer;
            scrollViewer?.ScrollToEnd();
        });
        Logger.Information(message);
    }

    public void SetStatus(string status, string colorKey = "GreenBrush")
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = status;
            try { StatusText.Foreground = FindResource(colorKey) as System.Windows.Media.Brush; }
            catch { }
        });
    }

    // ── Tool call satırlarını temizler ────────────────────────────────────────
    private static string StripToolCalls(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return response;
        var lines     = response.Split('\n');
        var cleaned   = new System.Collections.Generic.List<string>();
        foreach (var line in lines)
        {
            var trimmed   = line.Trim();
            bool isToolCall = System.Text.RegularExpressions.Regex.IsMatch(
                trimmed, @"^[a-z_]+\(.*\)\s*$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!isToolCall && !string.IsNullOrWhiteSpace(trimmed))
                cleaned.Add(trimmed);
        }
        return string.Join(" ", cleaned).Trim();
    }

    // ── Sağlık panelini günceller ──────────────────────────────────────────────
    /// <summary>
    /// Parse health data result string and update health panel UI.
    /// Implements Requirement 13 - Health Data Visualization in UI
    /// </summary>
    private void UpdateHealthPanel(string healthData)
    {
        try
        {
            // Parse health data string into lines
            var lines = healthData.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line) && 
                              !line.StartsWith("──") &&           // Skip separator lines
                              !line.StartsWith("[güncelleme:"))   // Skip update timestamp
                .ToList();

            // Get up to 5 key metric lines
            var metricControls = new[] { HealthMetric1, HealthMetric2, HealthMetric3, HealthMetric4, HealthMetric5 };
            
            // Reset all metrics
            foreach (var control in metricControls)
            {
                control.Text = "";
                control.Visibility = Visibility.Collapsed;
            }

            // Populate metrics (up to 5)
            int metricsToShow = Math.Min(lines.Count, 5);
            for (int i = 0; i < metricsToShow; i++)
            {
                metricControls[i].Text = lines[i];
                metricControls[i].Visibility = Visibility.Visible;
            }

            // Extract and set data age from the full health data string
            var ageMatch = System.Text.RegularExpressions.Regex.Match(
                healthData, @"\[güncelleme:\s*(.+?)\]");
            if (ageMatch.Success)
            {
                HealthDataAge.Text = $"Güncelleme: {ageMatch.Groups[1].Value}";
            }
            else
            {
                HealthDataAge.Text = "Güncelleme: bilinmiyor";
            }

            // Show health panel
            HealthPanel.Visibility = Visibility.Visible;

            // Focus health panel for 5600 milliseconds
            FocusHealthPanel();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to update health panel");
        }
    }

    /// <summary>
    /// Focus health panel for 5600 milliseconds then hide it.
    /// </summary>
    private void FocusHealthPanel()
    {
        // Cancel existing timer if running
        _healthPanelTimer?.Stop();

        // Create and start new timer
        _healthPanelTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(5600)
        };
        
        _healthPanelTimer.Tick += (s, e) =>
        {
            HealthPanel.Visibility = Visibility.Collapsed;
            _healthPanelTimer?.Stop();
        };

        _healthPanelTimer.Start();
    }

    // ── Tool Confirmation System ─────────────────────────────────────────────

    /// <summary>
    /// Pending confirmation data structure.
    /// Implements Requirement 21 - Tool Confirmation Timeout and Expiry
    /// </summary>
    private class PendingConfirmation
    {
        public string ToolName { get; set; } = "";
        public string JsonArgs { get; set; } = "";
        public string Reason { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// Checks if text is a confirmation phrase.
    /// Implements Requirement 20 - Tool Confirmation System for Risky Operations
    /// </summary>
    private bool IsConfirmationText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = text.Trim().ToLower();
        var confirmPhrases = new[] { "evet", "onayla", "tamam", "çalıştır", "gönder" };
        return confirmPhrases.Any(phrase => normalized.Contains(phrase));
    }

    /// <summary>
    /// Executes a confirmed tool after user approval.
    /// Implements Requirement 21 - Tool Confirmation Timeout and Expiry
    /// </summary>
    private async Task ExecuteConfirmedTool(PendingConfirmation pending)
    {
        // Check if confirmation has expired
        if (DateTime.Now > pending.ExpiresAt)
        {
            WriteLog("❌ HATA: Onay süresi doldu. Komutu tekrar isteyin.");
            SetStatus("ERROR", "RedBrush");
            _pendingConfirmation = null;
            return;
        }

        WriteLog($"✅ Onay alındı, çalıştırılıyor...");
        SetStatus("EXECUTING", "GreenBrush");

        // Clear pending confirmation
        _pendingConfirmation = null;

        try
        {
            // Execute the confirmed tool via GeminiService
            var result = await _geminiService.ExecuteConfirmedToolAsync(pending.ToolName, pending.JsonArgs);
            
            // Display result
            var displayResult = result.Length > 120 ? result[..120] + "..." : result;
            WriteLog($"✅ {pending.ToolName}: {displayResult}");
            SetStatus("READY", "GreenBrush");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "ExecuteConfirmedTool failed");
            WriteLog($"❌ HATA: {ex.Message}");
            SetStatus("ERROR", "RedBrush");
        }
    }

    // ── Tool Success Sound System ────────────────────────────────────────────

    /// <summary>
    /// Determines if a success sound should be played for a tool execution.
    /// Implements Task 19.1 - Success sound detection logic
    /// Implements Requirement 24 - Tool Success Sound Effects
    /// </summary>
    /// <param name="toolName">The name of the tool that was executed</param>
    /// <param name="result">The result string returned by the tool</param>
    /// <returns>True if success sound should be played</returns>
    private bool ShouldPlaySuccessSound(string toolName, string result)
    {
        // Don't play sound if muted (Task 19.2)
        if (_isMuted) return false;

        // Task 19.2: Error result detection - don't play sound for errors
        if (ResultLooksLikeError(result)) return false;

        // Task 19.1: Action tools that always play success sound
        var actionTools = new[] {
            "open_app",
            "add_calendar_event",
            "add_reminder",
            "delete_calendar_event",
            "remove_calendar_event"
        };

        if (actionTools.Contains(toolName))
            return true;

        // Task 19.1: send_whatsapp_message special case
        // Only play sound when send_now=true AND result contains "gönderildi"
        if (toolName == "send_whatsapp_message")
        {
            var resultLower = result?.ToLower() ?? "";
            return resultLower.Contains("gönderildi") || resultLower.Contains("gonderildi");
        }

        return false;
    }

    /// <summary>
    /// Detects if a result string looks like an error.
    /// Implements Task 19.2 - Error result detection
    /// Implements Requirement 24 - Tool Success Sound Effects
    /// </summary>
    /// <param name="result">The result string to check</param>
    /// <returns>True if result appears to be an error</returns>
    private bool ResultLooksLikeError(string result)
    {
        var text = (result ?? "").Trim().ToLower();
        if (string.IsNullOrEmpty(text)) return false;

        // Task 19.2: Error keywords detection
        var errorMarkers = new[] {
            "hata",
            "error",
            "alinamadi",
            "alınamadı",
            "bulunamadi",
            "bulunamadı",
            "acilamadi",
            "açılamadı",
            "tamamlanamadi",
            "tamamlanamadı",
            "gecersiz",
            "geçersiz",
            "izin gerekiyor",
            "izin gerekli",
            "baglanti",
            "bağlantı"
        };

        return errorMarkers.Any(marker => text.Contains(marker));
    }

    /// <summary>
    /// Plays a success sound using Console.Beep.
    /// Implements Task 19.1 - Success sound (800Hz 150ms)
    /// Reference: Python core/sound.py play_success() method
    /// </summary>
    private void PlaySuccessSound()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                // Single beep: 800Hz for 150ms (Python: 600Hz 100ms + 800Hz 150ms)
                // Simplified to single beep for C# implementation
                Console.Beep(800, 150);
            }
            catch (Exception ex)
            {
                // Beep may fail on some systems, log but don't crash
                Logger.Warning($"Failed to play success sound: {ex.Message}");
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        _wakeupListener.Stop();
        _ttsService.Dispose();
        _liveAudioService.Dispose();
        Logger.Information("JARVIS C# Edition shutting down...");
        Logger.CloseAndFlush();
        base.OnClosed(e);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ── 3-MODE SYSTEM: PASSIVE → ACTIVE → MUTED ──────────────────────────────
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Changes the current operating mode and updates system state accordingly.
    /// </summary>
    private async Task SetMode(JarvisMode newMode, string reason = "")
    {
        if (_currentMode == newMode) return;

        var oldMode = _currentMode;
        _currentMode = newMode;

        Logger.Information($"Mode change: {oldMode} → {newMode} ({reason})");

        switch (newMode)
        {
            case JarvisMode.PASSIVE:
                await EnterPassiveMode();
                break;

            case JarvisMode.ACTIVE:
                await EnterActiveMode(oldMode);
                break;

            case JarvisMode.MUTED:
                await EnterMutedMode();
                break;
        }

        UpdateHologramForMode();
    }

    /// <summary>
    /// PASSIVE Mode: Sleep/Standby. Only wake-word detection active.
    /// </summary>
    private async Task EnterPassiveMode()
    {
        WriteLog("💤 PASSIVE MODE: Uyku moduna geçildi. Beni uyandırmak için 'Jarvis' de veya el çırp.");
        SetStatus("PASSIVE", "CyanBrush");

        // Stop Gemini Live Session
        try
        {
            // Stop background audio tasks (and vision)
            _liveAudioService.StopRecording();
            _liveVisionService.StopStreaming();
            await _geminiService.StopLiveSessionAsync();
            Logger.Information("Gemini Live Session closed (PASSIVE mode)");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to stop Gemini session: {ex.Message}");
        }

        // Keep wake-word detection active
        _wakeupListener.ResumeClapDetector();

        // Speak farewell if TTS available
        if (!_isMuted)
        {
            await _ttsService.SpeakAsync("Uyuyorum. Beni çağırmak için Jarvis de veya el çırp.");
        }
    }

    /// <summary>
    /// ACTIVE Mode: Fully active. Continuously listening and responding.
    /// Implements Task 4.2 - Session persistence and mode-aware initialization.
    /// Requirements: 3.3, 5.1, 5.3-5.4, 5.6, 21.4
    /// </summary>
    /// <param name="fromMode">The previous mode (to determine session and message behavior)</param>
    private async Task EnterActiveMode(JarvisMode fromMode)
    {
        WriteLog("✅ ACTIVE MODE: Tam aktif mod. Sürekli dinliyorum.");
        SetStatus("ACTIVE", "GreenBrush");

        bool sessionAlreadyOpen = _geminiService.IsSessionOpen;
        
        // Check if Gemini session is already open (coming from MUTED)
        if (!sessionAlreadyOpen)
        {
            // Session closed - coming from PASSIVE, need to start session
            try
            {
                await _geminiService.StartLiveSessionAsync();
                _liveVisionService.StartStreaming();
                WriteLog("🌍 Live API Bağlandı!");
                Logger.Information("Gemini Live Session opened (ACTIVE mode)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start Gemini session");
                WriteLog($"[HATA] Live API başlatılamadı: {ex.Message}");
                return; // Exit if session failed to start
            }
        }
        else
        {
            // Session already open - coming from MUTED, skip session initialization
            Logger.Information("Gemini Live Session already open (ACTIVE mode)");
        }

        // Enable TTS service (disabled in MUTED mode)
        _isMuted = false;
        _liveAudioService.IsMuted = false;
        MuteButton.Content = "🔊 Mute";

        // Start LiveAudioService recording if not already running
        if (!_liveAudioService.IsRecording)
        {
            try
            {
                _liveAudioService.StartRecording();
                Logger.Information("LiveAudioService recording started (ACTIVE mode)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start recording");
                WriteLog($"[HATA] Mikrofon başlatılamadı: {ex.Message}");
            }
        }

        // Pause hardware wake-word detection (use transcription-based detection in ACTIVE)
        _wakeupListener.PauseClapDetector();

        // Speak different messages based on previous mode
        // NOTE: We send text to Gemini Live Session instead of using separate TTS API
        // This prevents API conflicts and uses the session's built-in TTS
        if (fromMode == JarvisMode.PASSIVE)
        {
            // Coming from PASSIVE - full greeting via Gemini Live
            try
            {
                await _geminiService.GenerateTextAsync("Efendim, dinliyorum.");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to send greeting: {ex.Message}");
            }
        }
        else if (fromMode == JarvisMode.MUTED)
        {
            // Coming from MUTED - short acknowledgment via Gemini Live
            try
            {
                await _geminiService.GenerateTextAsync("Efendim.");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to send greeting: {ex.Message}");
            }
        }

        // Update hologram color to green (0, 200, 180) - handled by CompositionTarget_Rendering
        // based on _currentMode which is already set to ACTIVE
    }

    /// <summary>
    /// MUTED Mode: Listening locally but not sending to cloud. Session open, TTS disabled.
    /// Microphone is released to WakeupListener (Vosk AI) for exclusive offline wake-word detection.
    /// Wakes up locally via Vosk AI or Clap Detector.
    /// </summary>
    private async Task EnterMutedMode()
    {
        WriteLog("🔇 MUTED MODE: Susturuldu. Vosk AI ile yerel olarak dinliyorum.");
        SetStatus("MUTED", "OrangeBrush");

        // Keep Gemini session running (don't close!)
        if (!_geminiService.IsSessionOpen)
        {
            try
            {
                await _geminiService.StartLiveSessionAsync();
                _liveVisionService.StartStreaming();
                Logger.Information("Gemini Live Session started (MUTED mode)");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to start Gemini session: {ex.Message}");
            }
        }

        // Enable system mute flag (controls TTS output)
        _isMuted = true;
        MuteButton.Content = "🔇 Unmute";

        // CRITICAL: Stop LiveAudioService recording to release microphone exclusively
        // to WakeupListener (Vosk AI). Two WaveIn on the same device can cause silent failures.
        if (_liveAudioService.IsRecording)
        {
            _liveAudioService.StopRecording();
            Logger.Information("LiveAudioService stopped — microphone released to Vosk AI.");
        }

        // Start WakeupListener with exclusive microphone access for Vosk AI + Clap detection
        _wakeupListener.ResumeClapDetector();
        Logger.Information("WakeupListener resumed — Vosk AI listening for 'Jarvis'...");
    }

    /// <summary>
    /// Updates hologram colors based on current mode.
    /// </summary>
    private void UpdateHologramForMode()
    {
        // Colors will be applied in CompositionTarget_Rendering
        // This method triggers a re-render
        _headActivity = 1.5;
    }

    /// <summary>
    /// Detects mode change commands in user text.
    /// Returns true if mode was changed, false otherwise.
    /// </summary>
    private async Task<bool> HandleModeChangeCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var normalized = text.Trim().ToLower();

        // Commands to enter PASSIVE mode (full shutdown - session closes)
        var sleepKeywords = new[] {
            "jarvis kapat",
            "jarvis uyku",
            "jarvis sleep",
            "jarvis uyku modu",
            "jarvis go to sleep",
            "jarvis passive"
        };

        foreach (var keyword in sleepKeywords)
        {
            if (normalized.Contains(keyword))
            {
                await SetMode(JarvisMode.PASSIVE, "user command: sleep");
                return true;
            }
        }

        // Commands to enter MUTED mode (keep listening, stop responding)
        var muteKeywords = new[] {
            "jarvis kapan",
            "jarvis sus",
            "jarvis sessiz",
            "jarvis sessiz ol",
            "jarvis konuşma",
            "jarvis mute",
            "sessiz ol",
            "sus artık",
            "sussana"
        };

        foreach (var keyword in muteKeywords)
        {
            if (normalized.Contains(keyword))
            {
                await SetMode(JarvisMode.MUTED, "user command: mute");
                return true;
            }
        }

        // Commands to return to ACTIVE mode (from MUTED)
        var unmuteKeywords = new[] {
            "jarvis konuş",
            "jarvis açıl",
            "jarvis unmute",
            "jarvis speak",
            "konuş artık",
            "sesini aç"
        };

        foreach (var keyword in unmuteKeywords)
        {
            if (normalized.Contains(keyword))
            {
                await SetMode(JarvisMode.ACTIVE, "user command: unmute");
                return true;
            }
        }

        // In MUTED mode: if user says "jarvis" (plain wake word), wake up
        // This is a fallback in case Gemini doesn't call change_mode tool
        if (_currentMode == JarvisMode.MUTED && CommandDetector.DetectCommandInTranscription(normalized) == CommandDetectionResult.WAKE_WORD)
        {
            WriteLog("👋 MUTED modda 'Jarvis' kelimesi tespit edildi — uyanıyorum!");
            await SetMode(JarvisMode.ACTIVE, "wake word in MUTED mode");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles wake-up from PASSIVE mode.
    /// Speaks "Efendim" and enters ACTIVE mode.
    /// </summary>
    private async Task HandleWakeUp()
    {
        if (_currentMode == JarvisMode.PASSIVE || _currentMode == JarvisMode.MUTED)
        {
            WriteLog($"👋 Wake-up detected! {_currentMode} → ACTIVE mode...");
            await SetMode(JarvisMode.ACTIVE, "wake-up");
        }
        else
        {
            // Already active - just acknowledge with sound
            WriteLog("⚙️ Dinleniyor...");
            SetStatus("LISTENING", "GreenBrush");
        }
    }

    /// <summary>
    /// Detects mode commands and wake words in transcription text with priority-based matching.
    /// Implements Requirements 2.1-2.4, 4.2-4.6, 8.1-8.6, 24.1-24.7.
    /// <summary>
    /// Detects commands in transcription text using priority-based matching.
    /// Delegates to CommandDetector utility class.
    /// Implements Requirements 2.1-2.4, 4.2-4.6, 8.1-8.6, 24.1-24.7.
    /// </summary>
    /// <param name="text">Raw transcription text to analyze</param>
    /// <returns>CommandDetectionResult enum indicating detected command type</returns>
    private CommandDetectionResult DetectCommandInTranscription(string text)
    {
        return CommandDetector.DetectCommandInTranscription(text);
    }

    /// <summary>
    /// Detects wake word in normalized transcription text.
    /// Checks cooldown before determining if wake word should trigger mode change.
    /// Implements Requirements 1.3-1.4, 4.2-4.3, 4.6.
    /// </summary>
    /// <param name="normalizedText">Normalized (lowercase, trimmed) transcription text</param>
    /// <returns>True if wake word detected and cooldown not active, false otherwise</returns>
    private bool DetectWakeWordInTranscription(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
            return false;

        // Check cooldown first (Requirement 10.2-10.4)
        if (IsInWakeWordCooldown())
        {
            Logger.Debug("Wake word suppressed during cooldown period");
            return false;
        }

        // Use CommandDetector to check for wake word
        var result = CommandDetector.DetectCommandInTranscription(normalizedText);
        if (result == CommandDetectionResult.WAKE_WORD)
        {
            Logger.Information($"Wake word detected from transcription: {normalizedText}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if wake word cooldown period is currently active.
    /// Implements Requirements 10.1-10.3.
    /// </summary>
    /// <returns>True if cooldown is active, false otherwise</returns>
    private bool IsInWakeWordCooldown()
    {
        return DateTime.UtcNow < _wakeWordCooldownUntil;
    }

    /// <summary>
    /// Starts wake word cooldown period.
    /// Implements Requirements 10.1, 10.6.
    /// Suppresses wake word detection for specified duration to prevent false positives.
    /// </summary>
    /// <param name="durationSeconds">Cooldown duration in seconds (default: 5)</param>
    private void StartWakeWordCooldown(int durationSeconds = 5)
    {
        _wakeWordCooldownUntil = DateTime.UtcNow.AddSeconds(durationSeconds);
        Logger.Information($"Wake word cooldown active until {_wakeWordCooldownUntil:HH:mm:ss.fff}");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow();
        settingsWindow.Owner = this;
        settingsWindow.ShowDialog();
    }
}
