using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.GenAI;
using Google.GenAI.Types;
using JarvisCSharp.Core;
using JarvisCSharp.Config;
using JarvisCSharp.Actions;

namespace JarvisCSharp.AI
{
    public class GeminiService
    {
        private readonly string _apiKey;
        private Client? _client;
        private AsyncSession? _liveSession;
        private bool _isSessionOpen = false;
        private bool _isReconnecting = false;
        private readonly string _baseSystemPrompt;
        private ActionManager? _actionManager;

        public bool IsSessionOpen => _isSessionOpen;

        public event Action<byte[]>? OnAudioReceived;
        public event Action<string>? OnTextReceived;
        public event Action<string, string>? OnToolCompleted; // (toolName, result)
        public event Action<string, string, string>? OnConfirmationNeeded; // (toolName, jsonArgs, reason)

        public GeminiService()
        {
            _apiKey = AppConfig.GetValue("gemini_api_key", "");

            if (string.IsNullOrEmpty(_apiKey))
            {
                Logger.Warning("Gemini API key not configured");
                throw new AIException("Gemini API key not configured");
            }

            _baseSystemPrompt = LoadSystemPrompt();
            InitializeModel();
        }

        public void SetActionManager(ActionManager actionManager)
        {
            _actionManager = actionManager;
        }

        private string LoadSystemPrompt()
        {
            try
            {
                string promptPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "config", "prompt.txt");
                if (System.IO.File.Exists(promptPath))
                {
                    var text = System.IO.File.ReadAllText(promptPath);
                    Logger.Information("System prompt loaded from config/prompt.txt");
                    return text;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not load prompt.txt: {ex.Message}");
            }
            return "Sen JARVIS'sin — Windows'ta çalışan kişisel AI asistanı. Türkçe konuş, kısa ve net ol.";
        }

        private void InitializeModel()
        {
            try
            {
                _client = new Client(apiKey: _apiKey);
                Logger.Information("Gemini client initialized.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize Gemini client");
                throw new AIException("Failed to initialize Gemini client", ex);
            }
        }

        public async Task StartLiveSessionAsync()
        {
            if (_client == null) throw new AIException("Client not initialized");

            try
            {
                string memoryContext = JarvisCSharp.Memory.MemoryManager.FormatMemoryForPrompt();
                string combinedSys = string.IsNullOrWhiteSpace(memoryContext)
                    ? _baseSystemPrompt
                    : $"{_baseSystemPrompt}\n\n{memoryContext}";

                string voiceName = AppConfig.GetValue("voice", "Charon");

                var config = new LiveConnectConfig
                {
                    ResponseModalities = new List<Modality> { Modality.Audio },
                    // OutputAudioTranscription = new AudioTranscriptionConfig(),
                    SystemInstruction = new Content
                    {
                        Role = "system",
                        Parts = new List<Part> { new Part { Text = combinedSys } }
                    },
                    SpeechConfig = new SpeechConfig
                    {
                        VoiceConfig = new VoiceConfig
                        {
                            PrebuiltVoiceConfig = new PrebuiltVoiceConfig
                            {
                                VoiceName = voiceName
                            }
                        }
                    },
                    Tools = new List<Tool> { new Tool { FunctionDeclarations = ToolSchema.GetDeclarations() } }
                };

                Logger.Information("Connecting to Gemini Live API...");
                Logger.Information($"Model: models/gemini-2.5-flash-native-audio-latest, Voice: {voiceName}");
                
                // Add explicit API key validation for detailed debugging
                try
                {
                    using var http = new System.Net.Http.HttpClient();
                    var validateResp = await http.GetAsync($"https://generativelanguage.googleapis.com/v1alpha/models?key={_apiKey}");
                    if (!validateResp.IsSuccessStatusCode)
                    {
                        string errBody = await validateResp.Content.ReadAsStringAsync();
                        Logger.Error(null!, $"[DEBUG] API Key Validation Failed! Status: {validateResp.StatusCode}");
                        Logger.Error(null!, $"[DEBUG] Error Details: {errBody}");
                        // Continue anyway, but now we have the detailed error in logs
                    }
                    else
                    {
                        Logger.Information("[DEBUG] API Key is valid.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"[DEBUG] Could not validate API key via HTTP: {ex.Message}");
                }

                _liveSession = await _client.Live.ConnectAsync("models/gemini-2.5-flash-native-audio-latest", config);
                
                if (_liveSession == null)
                {
                    Logger.Error(null!, "ConnectAsync returned null session!");
                    _isSessionOpen = false;
                    throw new AIException("Failed to connect: session is null");
                }
                
                _isSessionOpen = true;
                Logger.Information("Gemini Live API connected.");

                _ = Task.Run(ReceiveLoopAsync);
            }
            catch (Exception ex)
            {
                _isSessionOpen = false;
                Logger.Error(ex, "Failed to start Live API session");
                Logger.Error(ex, $"Exception details: Type={ex.GetType().Name}, Message={ex.Message}, StackTrace={ex.StackTrace}");
                throw;
            }
        }

        public async Task StopLiveSessionAsync()
        {
            if (_liveSession == null || !_isSessionOpen)
            {
                Logger.Information("Live session already closed or not started.");
                return;
            }

            try
            {
                Logger.Information("Closing Gemini Live API session...");
                
                // Dispose the session (closes connection)
                _liveSession = null;
                _isSessionOpen = false;
                
                Logger.Information("Gemini Live API session closed.");
                
                // Small delay to ensure cleanup
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Error closing Live session: {ex.Message}");
                _isSessionOpen = false;
            }
        }

        private async Task TryReconnectAsync()
        {
            if (_isReconnecting) return;
            _isReconnecting = true;
            _isSessionOpen = false;

            Logger.Warning("WebSocket disconnected. Attempting to reconnect...");

            int attempt = 0;
            while (attempt < 5)
            {
                attempt++;
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                    await StartLiveSessionAsync();
                    if (_isSessionOpen)
                    {
                        Logger.Information($"Reconnected successfully on attempt {attempt}.");
                        _isReconnecting = false;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Reconnect attempt {attempt} failed: {ex.Message}");
                }
            }

            Logger.Error(null!, "Could not reconnect to Gemini Live API after 5 attempts.");
            _isReconnecting = false;
        }

        private async Task ReceiveLoopAsync()
        {
            if (_liveSession == null) return;

            try
            {
                Logger.Information("ReceiveLoopAsync started, waiting for responses...");
                int messageCount = 0;
                
                while (_isSessionOpen)
                {
                    try
                    {
                        Logger.Debug($"Calling ReceiveAsync (message #{messageCount})...");
                        var response = await _liveSession.ReceiveAsync();
                        
                        if (response == null)
                        {
                            Logger.Information($"ReceiveAsync returned null after {messageCount} messages, breaking receive loop");
                            break;
                        }
                        
                        messageCount++;
                        Logger.Debug($"Received response #{messageCount}");

                    // Tool call (function call) handler
                        var toolCall = response.ToolCall;
                        if (toolCall?.FunctionCalls != null)
                        {
                            foreach (var fc in toolCall.FunctionCalls)
                            {
                                _ = Task.Run(() => HandleFunctionCallAsync(fc));
                            }
                        }

                        var modelTurn = response.ServerContent?.ModelTurn;
                        if (modelTurn?.Parts != null)
                        {
                            foreach (var part in modelTurn.Parts)
                            {
                                if (part.InlineData?.Data != null)
                                {
                                    OnAudioReceived?.Invoke(part.InlineData.Data);
                                }
                                if (!string.IsNullOrEmpty(part.Text))
                                {
                                    // Thinking/reasoning bloklarını filtrele
                                    var text = part.Text.Trim();
                                    if (!text.StartsWith("**"))
                                    {
                                        OnTextReceived?.Invoke(text);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception partEx)
                    {
                        Logger.Warning($"Error processing response part: {partEx.Message}");
                    }
                }
                
                Logger.Information($"ReceiveLoopAsync exiting normally after {messageCount} messages");
            }
            catch (OperationCanceledException)
            {
                Logger.Information("Receive loop cancelled");
                _isSessionOpen = false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Live API receive loop error");
                Logger.Warning($"Exception type: {ex.GetType().Name}, Message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Logger.Warning($"Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                }
                _isSessionOpen = false;
                // Don't auto-reconnect - let MainWindow handle session state based on mode
            }
        }

        private async Task HandleFunctionCallAsync(FunctionCall fc)
        {
            if (_liveSession == null || !_isSessionOpen) return;

            var name = fc.Name ?? "";
            var args = fc.Args;

            Logger.Information($"[TOOL] Calling: {name}");
            OnTextReceived?.Invoke($"🔧 {name} çalıştırılıyor...");

            string result = "Tamam.";
            try
            {
                if (_actionManager != null)
                {
                    // fc.Args'ı güvenli şekilde JSON string'e çevir
                    string jsonArgs = SerializeArgs(args);
                    result = await _actionManager.ExecuteToolAsync(name, jsonArgs);
                    
                    // Check if result is a confirmation request (Requirement 20, 30)
                    if (result.StartsWith("CONFIRM:"))
                    {
                        var reason = result.Substring("CONFIRM:".Length).Trim();
                        OnConfirmationNeeded?.Invoke(name, jsonArgs, reason);
                        OnTextReceived?.Invoke(result); // Also send to UI for display
                        
                        // Send back to Gemini that confirmation is needed
                        result = $"Onay bekleniyor: {reason}. Kullanıcı 45 saniye içinde onaylamalı.";
                    }
                    else
                    {
                        // Normal result - show to user
                        var displayResult = result.Length > 80 ? result[..80] + "..." : result;
                        OnTextReceived?.Invoke($"✅ {name}: {displayResult}");
                    }
                }
                else
                {
                    result = "ActionManager bağlı değil.";
                    Logger.Warning("HandleFunctionCallAsync: ActionManager is null");
                }
            }
            catch (Exception ex)
            {
                result = $"Hata: {ex.Message}";
                Logger.Error(ex, $"Tool call failed: {name}");
            }

            // Sonucu Gemini'ye geri gönder
            try
            {
                var functionResponse = new FunctionResponse
                {
                    Name     = name,
                    Id       = fc.Id,
                    Response = new Dictionary<string, object> { ["output"] = result }
                };

                var toolResponse = new LiveSendToolResponseParameters
                {
                    FunctionResponses = new List<FunctionResponse> { functionResponse }
                };

                await _liveSession.SendToolResponseAsync(toolResponse);
                Logger.Information($"[TOOL] {name} -> {result[..Math.Min(80, result.Length)]}");
                
                // Fire tool completed event for UI updates (only for non-confirmation results)
                if (!result.StartsWith("Onay bekleniyor:"))
                {
                    OnToolCompleted?.Invoke(name, result);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to send tool response: {ex.Message}");
            }
        }

        /// <summary>
        /// FunctionCall.Args'ı JSON string'e güvenli şekilde çevirir.
        /// SDK versiyonuna göre args tipi değişebilir (IDictionary, JsonElement, Struct).
        /// </summary>
        private static string SerializeArgs(object? args)
        {
            if (args == null) return "{}";

            try
            {
                // Zaten string ise direkt kullan
                if (args is string s) return string.IsNullOrWhiteSpace(s) ? "{}" : s;

                // System.Text.Json ile serialize et
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };
                var json = System.Text.Json.JsonSerializer.Serialize(args, options);

                // Sonuç geçerli JSON object mu?
                if (json.StartsWith("{") || json.StartsWith("["))
                    return json;

                return "{}";
            }
            catch (Exception ex)
            {
                Logger.Warning($"SerializeArgs failed: {ex.Message}");
                return "{}";
            }
        }

        public async Task SendAudioAsync(byte[] pcmData)
        {
            if (_liveSession == null || !_isSessionOpen) return;
            try
            {
                var req = new LiveSendRealtimeInputParameters
                {
                    Audio = new Blob { MimeType = "audio/pcm;rate=16000", Data = pcmData }
                };
                await _liveSession.SendRealtimeInputAsync(req);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to send audio chunk: {ex.Message}");
                // Mark session as closed if WebSocket fails to prevent spam warnings
                if (ex.Message.Contains("WebSocket") || ex.Message.Contains("not open"))
                {
                    _isSessionOpen = false;
                    Logger.Information("Session marked as closed due to WebSocket error");
                }
            }
        }

        public async Task SendVideoFrameAsync(byte[] jpegData)
        {
            if (_liveSession == null || !_isSessionOpen) return;
            try
            {
                var req = new LiveSendRealtimeInputParameters
                {
                    Video = new Blob { MimeType = "image/jpeg", Data = jpegData }
                };
                await _liveSession.SendRealtimeInputAsync(req);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to send video frame: {ex.Message}");
                if (ex.Message.Contains("WebSocket") || ex.Message.Contains("not open"))
                {
                    _isSessionOpen = false;
                }
            }
        }

        public async Task SendAudioStreamEndAsync()
        {
            if (_liveSession == null || !_isSessionOpen) return;
            try
            {
                var req = new LiveSendRealtimeInputParameters { AudioStreamEnd = true };
                await _liveSession.SendRealtimeInputAsync(req);
            }
            catch { }
        }

        public async Task GenerateTextAsync(string text)
        {
            if (_liveSession == null || !_isSessionOpen)
            {
                throw new InvalidOperationException("Gemini Live session is not open. Cannot send message.");
            }

            var req = new LiveSendClientContentParameters
            {
                Turns = new List<Content> { new Content { Parts = new List<Part> { new Part { Text = text } } } },
                TurnComplete = true
            };
            await _liveSession.SendClientContentAsync(req);
        }

        /// <summary>
        /// Executes a confirmed tool with _confirmed_by_user flag added to arguments.
        /// Implements Requirement 21 - Tool Confirmation Timeout and Expiry
        /// </summary>
        public async Task<string> ExecuteConfirmedToolAsync(string toolName, string jsonArgs)
        {
            if (_actionManager == null)
            {
                Logger.Warning("ExecuteConfirmedToolAsync: ActionManager is null");
                return "ActionManager bağlı değil.";
            }

            try
            {
                // Parse JSON args
                var args = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(jsonArgs);
                if (args == null)
                {
                    args = new Dictionary<string, System.Text.Json.JsonElement>();
                }

                // Add confirmation flag
                args["_confirmed_by_user"] = System.Text.Json.JsonSerializer.SerializeToElement(true);

                // Serialize back to JSON
                var confirmedJsonArgs = System.Text.Json.JsonSerializer.Serialize(args);

                // Execute tool
                Logger.Information($"[CONFIRMED TOOL] Executing: {toolName}");
                var result = await _actionManager.ExecuteToolAsync(toolName, confirmedJsonArgs);
                
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"ExecuteConfirmedToolAsync failed for {toolName}");
                return $"Hata: {ex.Message}";
            }
        }
    }
}
