using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Actions
{
    /// <summary>
    /// Gemini tool call'larını ilgili action'a yönlendirir ve sonucu döndürür.
    /// </summary>
    public class ActionManager
    {
        private readonly IEnumerable<IAction> _actions;
        
        // Task 18.1: ThreadPoolExecutor with 32 worker threads (Requirement 22)
        private readonly SemaphoreSlim _workerSemaphore;
        
        public event Action<string>? OnModeChangeRequested;

        // Task 18.2: Per-tool timeout configuration (Requirement 22)
        private readonly Dictionary<string, int> _toolTimeouts;
        private const int DefaultTimeout = 120; // seconds

        public ActionManager(IEnumerable<IAction> actions)
        {
            _actions = actions;
            Logger.Information($"ActionManager initialized with {_actions.Count()} actions");
            
            // Task 18.1: Initialize ThreadPoolExecutor equivalent with 32 workers
            _workerSemaphore = new SemaphoreSlim(32, 32);
            
            // Task 18.2: Configure per-tool timeouts
            _toolTimeouts = new Dictionary<string, int>
            {
                { "shell_run", 45 },                  // 45 seconds for shell commands
                { "send_whatsapp_message", 60 },      // 60 seconds for WhatsApp
                { "analyze_screen", 150 }             // 150 seconds for screen analysis
            };
        }

        /// <summary>
        /// Gemini'den gelen tool call'ı çalıştırır.
        /// jsonArgs: Gemini'nin gönderdiği raw JSON args string'i.
        /// Dönüş: Gemini'ye iletilecek sonuç string'i.
        /// </summary>
        public async Task<string> ExecuteToolAsync(string toolName, string jsonArgs)
        {
            try
            {
                var args = ParseArgs(jsonArgs);
                Logger.Information($"[Tool] {toolName} {jsonArgs[..Math.Min(80, jsonArgs.Length)]}");

                // Risk assessment and confirmation check (Requirement 20)
                var confirmationReason = AssessConfirmationNeed(toolName, args);
                if (!string.IsNullOrEmpty(confirmationReason))
                {
                    Logger.Information($"[Confirmation Required] {toolName}: {confirmationReason}");
                    return $"CONFIRM:{confirmationReason}";
                }

                // Task 18.2 & 18.3: Get timeout for this tool (Requirement 22)
                var timeoutSeconds = _toolTimeouts.GetValueOrDefault(toolName, DefaultTimeout);
                
                // Task 18.1: Acquire worker thread semaphore (Requirement 22)
                await _workerSemaphore.WaitAsync();
                
                try
                {
                    // Task 18.3: Execute tool with timeout using Task.WaitAsync (Requirement 22)
                    var executionTask = toolName switch
                    {
                        "open_app"                   => Dispatch("open_app",          S(args, "app_name")),
                        "sys_info"                   => Dispatch("sys_info",           S(args, "query", "all")),
                        "get_weather"                => Dispatch("get_weather",        S(args, "location", "Istanbul")),
                        "get_health_data"            => Dispatch("get_health_data",    S(args, "query", "all")),
                        "browser_control"            => ExecBrowser(args),
                        "shell_run"                  => Dispatch("shell_run",          S(args, "command")),
                        "play_media"                 => ExecMedia(args),
                        "get_youtube_channel_report" => ExecYoutube(args),
                        "analyze_screen"             => ExecScreenVision(args),
                        "save_memory"                => Dispatch("save_memory",        jsonArgs),
                        "delete_memory"              => Dispatch("delete_memory",      jsonArgs),
                        "send_whatsapp_message"      => ExecWhatsapp(args),
                        "save_whatsapp_contact"      => ExecSaveWhatsappContact(args),
                        "clipboard_action"           => ExecClipboard(args),
                        "desktop_control"            => ExecDesktop(args),
                        "get_calendar_events"        => ExecCalendarGet(args),
                        "add_calendar_event"         => ExecCalendarAdd(args),
                        "delete_calendar_event"      => ExecCalendarDelete(args),
                        "get_reminders"              => ExecRemindersGet(args),
                        "add_reminder"               => ExecRemindersAdd(args),
                        "change_mode"                => ExecChangeMode(args),

                        "click_element"              => ExecAdvanced("click_element", args),
                        "type_text"                  => ExecAdvanced("type_text", args),
                        "send_shortcut"              => ExecAdvanced("send_shortcut", args),
                        "multi_step_workflow"        => ExecAdvanced("multi_step_workflow", args),
                        "extract_text"               => ExecAdvanced("extract_text", args),
                        "launch_app"                 => ExecAdvanced("launch_app", args),
                        "switch_window"              => ExecAdvanced("switch_window", args),
                        "maximize_window"            => ExecAdvanced("maximize_window", args),
                        "minimize_window"            => ExecAdvanced("minimize_window", args),
                        "close_window"               => ExecAdvanced("close_window", args),
                        "move_window"                => ExecAdvanced("move_window", args),
                        "correct_detection"          => ExecAdvanced("correct_detection", args),
                        "learn_alias"                => ExecAdvanced("learn_alias", args),

                        _                            => ExecGeneric(toolName, jsonArgs)
                    };
                    
                    // Task 18.3: Apply timeout with WaitAsync (Requirement 22, 23)
                    return await executionTask.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
                }
                finally
                {
                    // Task 18.1: Release worker thread semaphore
                    _workerSemaphore.Release();
                }
            }
            catch (TimeoutException)
            {
                // Task 18.3: Handle timeout exception (Requirement 22, 23)
                var timeoutSeconds = _toolTimeouts.GetValueOrDefault(toolName, DefaultTimeout);
                Logger.Error($"[Timeout] {toolName} zaman aşımına uğradı ({timeoutSeconds} saniye)");
                return $"Hata: {toolName} işlemi zaman aşımına uğradı ({timeoutSeconds} saniye).";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"ActionManager: {toolName} failed");
                return $"Hata: {ex.Message}";
            }
        }

        // ── Risk Assessment & Confirmation ────────────────────────────────────

        /// <summary>
        /// Assesses if a tool requires user confirmation based on risk.
        /// Returns confirmation reason string if confirmation is needed, empty string otherwise.
        /// Implements Requirement 20 - Tool Confirmation System for Risky Operations
        /// </summary>
        private string? AssessConfirmationNeed(string toolName, Dictionary<string, JsonElement> args)
        {
            // Kullanıcının isteği üzerine onay sistemi tamamen devre dışı bırakıldı.
            return null;
        }

        // ── Yardımcı ──────────────────────────────────────────────────────────

        private IAction? Find(string name)
            => _actions.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private async Task<string> Dispatch(string actionName, string payload)
        {
            var action = Find(actionName);
            return action == null
                ? $"Hata: '{actionName}' action bulunamadı."
                : await action.ExecuteAsync(payload);
        }

        private static Dictionary<string, JsonElement> ParseArgs(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try { return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new(); }
            catch { return new(); }
        }

        private static string S(Dictionary<string, JsonElement> args, string key, string def = "")
        {
            if (!args.TryGetValue(key, out var v)) return def;
            return v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : v.ToString();
        }

        private static bool B(Dictionary<string, JsonElement> args, string key, bool def = false)
        {
            if (!args.TryGetValue(key, out var v)) return def;
            return v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.False && def);
        }

        private static int I(Dictionary<string, JsonElement> args, string key, int def = 0)
        {
            if (!args.TryGetValue(key, out var v)) return def;
            if (v.TryGetInt32(out var i)) return i;
            if (v.TryGetDouble(out var d)) return (int)d;
            return def;
        }

        // ── Dispatcher'lar ────────────────────────────────────────────────────

        private async Task<string> ExecBrowser(Dictionary<string, JsonElement> args)
        {
            var action = S(args, "action", "search");
            var payload = action switch
            {
                "open_url"     => $"open_url:{S(args, "url")}",
                "play_youtube" => $"play_youtube:{S(args, "query")}",
                _              => $"search:{S(args, "query")}"
            };
            return await Dispatch("browser_control", payload);
        }

        private async Task<string> ExecMedia(Dictionary<string, JsonElement> args)
        {
            var provider = S(args, "provider", "auto").ToLower();
            var query    = S(args, "query");
            var payload  = provider == "spotify" ? $"spotify:{query}" : query;
            return await Dispatch("play_media", payload);
        }

        private async Task<string> ExecYoutube(Dictionary<string, JsonElement> args)
        {
            var payload = JsonSerializer.Serialize(new {
                query       = S(args, "query", "overview"),
                handle      = S(args, "handle"),
                video_limit = I(args, "video_limit", 6)
            });
            return await Dispatch("youtube_stats", payload);
        }

        private async Task<string> ExecScreenVision(Dictionary<string, JsonElement> args)
        {
            var payload = JsonSerializer.Serialize(new {
                query  = S(args, "query", "Ekranda ne var?"),
                target = S(args, "target", "active_window")
            });
            return await Dispatch("analyze_screen", payload);
        }

        private async Task<string> ExecWhatsapp(Dictionary<string, JsonElement> args)
        {
            // Create JSON payload for WhatsAppAction with all parameters
            var payload = JsonSerializer.Serialize(new {
                message        = S(args, "message"),
                phone_number   = S(args, "phone_number"),
                recipient_name = S(args, "recipient_name"),
                send_now       = B(args, "send_now", false),
                app_target     = S(args, "app_target", "auto")
            });

            return await Dispatch("send_whatsapp_message", payload);
        }

        private async Task<string> ExecSaveWhatsappContact(Dictionary<string, JsonElement> args)
        {
            var displayName = S(args, "display_name");
            var phoneNumber = S(args, "phone_number");
            var aliases     = S(args, "aliases", "");

            if (string.IsNullOrEmpty(displayName))
                return "Kişi adı boş olamaz.";
            if (string.IsNullOrEmpty(phoneNumber))
                return "Telefon numarası gerekli.";

            var result = WhatsappAction.SaveWhatsappContact(displayName, phoneNumber, aliases);
            return await Task.FromResult(result);
        }

        private async Task<string> ExecClipboard(Dictionary<string, JsonElement> args)
        {
            var action  = S(args, "action", "read");
            var payload = action switch
            {
                "write" => $"write:{S(args, "text")}",
                "smart" => $"smart:{S(args, "instruction")}",
                _       => "read:"
            };
            return await Dispatch("clipboard_action", payload);
        }

        private async Task<string> ExecDesktop(Dictionary<string, JsonElement> args)
        {
            var action  = S(args, "action");
            var value   = S(args, "value");
            var payload = string.IsNullOrEmpty(value) ? action : $"{action}:{value}";
            return await Dispatch("desktop_control", payload);
        }

        private async Task<string> ExecCalendarGet(Dictionary<string, JsonElement> args)
            => await Dispatch("calendar_action", $"get:{S(args, "query", "today")}:{I(args, "limit", 6)}");

        private async Task<string> ExecCalendarAdd(Dictionary<string, JsonElement> args)
        {
            var payload = JsonSerializer.Serialize(new {
                title         = S(args, "title"),
                start_iso     = S(args, "start_iso"),
                end_iso       = S(args, "end_iso"),
                location      = S(args, "location"),
                notes         = S(args, "notes"),
                calendar_name = S(args, "calendar_name"),
                all_day       = B(args, "all_day")
            });
            return await Dispatch("calendar_action", $"add:{payload}");
        }

        private async Task<string> ExecCalendarDelete(Dictionary<string, JsonElement> args)
            => await Dispatch("calendar_action", $"delete:{S(args, "title")}:{S(args, "start_iso")}");

        private async Task<string> ExecRemindersGet(Dictionary<string, JsonElement> args)
            => await Dispatch("reminders_action", $"get:{S(args, "query", "upcoming")}:{S(args, "list_name")}");

        private async Task<string> ExecRemindersAdd(Dictionary<string, JsonElement> args)
        {
            var payload = JsonSerializer.Serialize(new {
                title     = S(args, "title"),
                due_iso   = S(args, "due_iso"),
                notes     = S(args, "notes"),
                list_name = S(args, "list_name"),
                priority  = S(args, "priority"),
                all_day   = B(args, "all_day")
            });
            return await Dispatch("reminders_action", $"add:{payload}");
        }

        private async Task<string> ExecChangeMode(Dictionary<string, JsonElement> args)
        {
            var mode = S(args, "mode", "muted");
            OnModeChangeRequested?.Invoke(mode);
            return $"Mod '{mode}' olarak değiştirildi.";
        }

        private async Task<string> ExecAdvanced(string subAction, Dictionary<string, JsonElement> args)
        {
            var payloadObj = new Dictionary<string, object> { { "sub_action", subAction } };
            foreach (var kv in args)
            {
                payloadObj[kv.Key] = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.ToString();
            }
            var payload = JsonSerializer.Serialize(payloadObj);
            return await Dispatch("advanced_window_control", payload);
        }

        private async Task<string> ExecGeneric(string toolName, string jsonArgs)
        {
            var action = Find(toolName);
            if (action == null)
            {
                Logger.Warning($"[ActionManager] Unknown tool: {toolName}");
                return $"Bilinmeyen araç: {toolName}";
            }
            return await action.ExecuteAsync(jsonArgs);
        }
    }
}
