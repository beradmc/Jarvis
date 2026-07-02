using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JarvisCSharp.Config
{
    public class AutomationSettings
    {
        [JsonPropertyName("confirmation_level")]
        public string ConfirmationLevel { get; set; } = "medium"; // "low", "medium", "high"

        [JsonPropertyName("automation_speed")]
        public double AutomationSpeed { get; set; } = 1.0; // 0.5 to 2.0

        [JsonPropertyName("timeouts_ms")]
        public int TimeoutsMs { get; set; } = 5000;

        [JsonPropertyName("sandbox_mode")]
        public bool SandboxMode { get; set; } = false;

        [JsonPropertyName("application_overrides")]
        public Dictionary<string, AutomationSettings> ApplicationOverrides { get; set; } = new Dictionary<string, AutomationSettings>();

        public void Validate()
        {
            // Fallback to defaults if invalid
            if (ConfirmationLevel != "low" && ConfirmationLevel != "medium" && ConfirmationLevel != "high")
            {
                Core.Logger.Warning($"Invalid ConfirmationLevel '{ConfirmationLevel}', defaulting to 'medium'");
                ConfirmationLevel = "medium";
            }

            if (AutomationSpeed < 0.1 || AutomationSpeed > 5.0)
            {
                Core.Logger.Warning($"Invalid AutomationSpeed '{AutomationSpeed}', defaulting to 1.0");
                AutomationSpeed = 1.0;
            }

            if (TimeoutsMs < 500 || TimeoutsMs > 60000)
            {
                Core.Logger.Warning($"Invalid TimeoutsMs '{TimeoutsMs}', defaulting to 5000");
                TimeoutsMs = 5000;
            }

            if (ApplicationOverrides != null)
            {
                foreach (var kvp in ApplicationOverrides)
                {
                    kvp.Value.Validate();
                }
            }
        }
    }

    public class AppConfig
    {
        [JsonPropertyName("gemini_api_key")]
        public string Gemini_Api_Key { get; set; } = "";
        
        [JsonPropertyName("voice")]
        public string Voice { get; set; } = "Charon";
        
        [JsonPropertyName("weather_api_key")]
        public string Weather_Api_Key { get; set; } = "";
        
        [JsonPropertyName("youtube_api_key")]
        public string Youtube_Api_Key { get; set; } = "";
        
        [JsonPropertyName("youtube_channel_handle")]
        public string Youtube_Channel_Handle { get; set; } = "";

        [JsonPropertyName("automation")]
        public AutomationSettings Automation { get; set; } = new AutomationSettings();

        // Note: Keep api_keys.json for legacy compatibility, though we added automation settings.
        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "config", "api_keys.json");

        private static AppConfig? _cache;
        private static DateTime _cacheTime = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
        private static readonly object _lock = new object();

        // ── Yükleme ───────────────────────────────────────────────────────────

        public static AppConfig Load()
        {
            lock (_lock)
            {
                if (_cache != null && (DateTime.UtcNow - _cacheTime) < CacheTtl)
                    return _cache;

                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        var json = File.ReadAllText(ConfigPath);
                        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var cfg = JsonSerializer.Deserialize<AppConfig>(json, opts);
                        if (cfg != null)
                        {
                            if (cfg.Automation == null)
                                cfg.Automation = new AutomationSettings();
                                
                            cfg.Automation.Validate(); // Fallback on invalid values
                            
                            _cache = cfg;
                            _cacheTime = DateTime.UtcNow;
                            return _cache;
                        }
                    }
                }
                catch (JsonException jex)
                {
                    Core.Logger.Error(jex, "AppConfig JSON syntax error. Falling back to defaults.");
                }
                catch (Exception ex)
                {
                    Core.Logger.Warning($"AppConfig load failed: {ex.Message}");
                }

                return _cache ?? new AppConfig();
            }
        }

        public static void InvalidateCache()
        {
            lock (_lock) { _cache = null; _cacheTime = DateTime.MinValue; }
        }

        // ── Kaydetme ──────────────────────────────────────────────────────────

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Re-validate before save
                if (Automation == null) Automation = new AutomationSettings();
                Automation.Validate();

                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, opts));

                lock (_lock) { _cache = this; _cacheTime = DateTime.UtcNow; }
            }
            catch (Exception ex)
            {
                Core.Logger.Error(ex, "Failed to save config");
            }
        }

        // ── Yardımcı ─────────────────────────────────────────────────────────

        public static string GetValue(string key, string defaultValue = "")
        {
            var c = Load();
            return key.ToLower() switch
            {
                "gemini_api_key"          => c.Gemini_Api_Key,
                "voice"                   => c.Voice,
                "weather_api_key"         => c.Weather_Api_Key,
                "youtube_api_key"         => c.Youtube_Api_Key,
                "youtube_channel_handle"  => c.Youtube_Channel_Handle,
                _                         => defaultValue
            };
        }

        public static bool HasGeminiApiKey()
            => !string.IsNullOrWhiteSpace(GetValue("gemini_api_key"));
    }
}
