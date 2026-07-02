using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    public class SafetyGuardianService : ISafetyGuardianService
    {
        private readonly Dictionary<string, HashSet<string>> _whitelist;
        private readonly string _whitelistPath;
        private bool _sandboxMode = false;

        // Destructive action keywords (Turkish + English)
        private static readonly string[] DestructiveKeywords = new[]
        {
            "delete", "remove", "sil", "kaldır", "format", "kill",
            "terminate", "sonlandır", "uninstall", "kapat", "close_without_save",
            "empty_recycle_bin", "çöp_kutusu_boşalt", "shutdown", "restart",
            "registry", "system_restore", "disk_format"
        };

        // Caution action keywords
        private static readonly string[] CautionKeywords = new[]
        {
            "type", "yaz", "write", "send", "gönder", "click", "tıkla",
            "move", "taşı", "rename", "yeniden_adlandır", "paste", "yapıştır",
            "cut", "kes", "close", "kapat", "modify", "değiştir"
        };

        // Sensitive window title patterns
        private static readonly string[] SensitiveWindowPatterns = new[]
        {
            "bank", "banka", "payment", "ödeme", "password", "şifre",
            "credential", "kimlik", "admin", "yönetici", "registry",
            "system32", "cmd", "powershell", "terminal", "regedit",
            "disk management", "disk yönetimi", "gpedit", "services.msc"
        };

        // Turkish confirmation phrases
        private static readonly string[] ConfirmationPhrases = new[]
        {
            "evet", "onayla", "tamam", "evet onaylıyorum", "onaylıyorum",
            "yes", "confirm", "ok", "okay", "proceed", "devam"
        };

        private Func<string, Task<string?>>? _confirmationCallback;

        public SafetyGuardianService(Func<string, Task<string?>>? confirmationCallback = null)
        {
            _confirmationCallback = confirmationCallback;
            _whitelist = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JarvisCSharp");
            Directory.CreateDirectory(appData);
            _whitelistPath = Path.Combine(appData, "safety_whitelist.json");

            LoadWhitelist();
        }

        public bool SandboxMode
        {
            get => _sandboxMode;
            set => _sandboxMode = value;
        }

        public RiskLevel ClassifyAction(string actionType, Dictionary<string, object> parameters, WindowInfo? targetWindow = null)
        {
            var actionLower = actionType.ToLower();

            // Check for destructive keywords in action and parameters
            bool isDestructive = DestructiveKeywords.Any(k => actionLower.Contains(k));

            if (!isDestructive)
            {
                foreach (var param in parameters.Values)
                {
                    var paramStr = param?.ToString()?.ToLower() ?? "";
                    if (DestructiveKeywords.Any(k => paramStr.Contains(k)))
                    {
                        isDestructive = true;
                        break;
                    }
                }
            }

            // Check if targeting a sensitive window
            bool isSensitiveTarget = false;
            if (targetWindow != null)
            {
                var titleLower = targetWindow.Title.ToLower();
                var processLower = targetWindow.ProcessName.ToLower();
                isSensitiveTarget = SensitiveWindowPatterns.Any(p =>
                    titleLower.Contains(p) || processLower.Contains(p));
            }

            if (isDestructive || isSensitiveTarget)
            {
                return RiskLevel.Destructive;
            }

            bool isCaution = CautionKeywords.Any(k => actionLower.Contains(k));
            if (isCaution)
            {
                return RiskLevel.Caution;
            }

            return RiskLevel.Safe;
        }

        public bool RequiresConfirmation(RiskLevel risk, string actionType)
        {
            return risk switch
            {
                RiskLevel.Safe => false,
                RiskLevel.Caution => true,
                RiskLevel.Destructive => true,
                _ => true
            };
        }

        public async Task<ConfirmationResult> RequestConfirmationAsync(string actionDescription, RiskLevel risk, int timeoutSeconds = 45)
        {
            var result = new ConfirmationResult
            {
                RequestTime = DateTime.UtcNow
            };

            // Safe actions auto-approve
            if (risk == RiskLevel.Safe)
            {
                result.Confirmed = true;
                result.UserResponse = "[auto-approved]";
                result.ResponseTime = DateTime.UtcNow;
                return result;
            }

            if (_confirmationCallback == null)
            {
                // No callback - log and auto-deny destructive, auto-approve caution
                Logger.Warning($"[SafetyGuardian] No confirmation callback. Action: {actionDescription}, Risk: {risk}");
                result.Confirmed = (risk == RiskLevel.Caution);
                result.UserResponse = result.Confirmed ? "[auto-approved-caution]" : "[auto-denied-no-callback]";
                result.ResponseTime = DateTime.UtcNow;
                return result;
            }

            // Build confirmation message
            string riskEmoji = risk == RiskLevel.Destructive ? "🔴" : "🟡";
            string riskLabel = risk == RiskLevel.Destructive ? "TEHLİKELİ" : "DİKKAT";
            string prompt = $"{riskEmoji} [{riskLabel}] {actionDescription}\n";
            prompt += risk == RiskLevel.Destructive
                ? "Bu işlemi onaylamak için 'evet onaylıyorum' yazın:"
                : "Devam etmek için 'evet' veya 'tamam' yazın:";

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var responseTask = _confirmationCallback(prompt);
                var completedTask = await Task.WhenAny(responseTask, Task.Delay(timeoutSeconds * 1000, cts.Token));

                if (completedTask == responseTask)
                {
                    var response = await responseTask;
                    result.UserResponse = response ?? "";
                    result.ResponseTime = DateTime.UtcNow;

                    var responseLower = result.UserResponse.Trim().ToLower();
                    result.Confirmed = ConfirmationPhrases.Any(p => responseLower.Contains(p));
                }
                else
                {
                    result.TimedOut = true;
                    result.Confirmed = false;
                    result.UserResponse = "[timeout]";
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SafetyGuardian] Confirmation error");
                result.Confirmed = false;
                result.UserResponse = $"[error: {ex.Message}]";
            }

            return result;
        }

        public bool IsWhitelisted(string actionType, WindowInfo targetWindow)
        {
            if (_whitelist.TryGetValue(actionType, out var windowTitles))
            {
                return windowTitles.Any(pattern =>
                    targetWindow.Title.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pattern == "*");
            }
            return false;
        }

        public void AddToWhitelist(string actionType, string windowTitle)
        {
            if (!_whitelist.ContainsKey(actionType))
            {
                _whitelist[actionType] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            _whitelist[actionType].Add(windowTitle);
            SaveWhitelist();
        }

        public SimulationResult SimulateAction(string actionType, Dictionary<string, object> parameters)
        {
            var risk = ClassifyAction(actionType, parameters);
            var paramsStr = string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"));

            var result = new SimulationResult
            {
                ActionDescription = $"[SANDBOX] {actionType}({paramsStr})",
                Risk = risk,
                ExpectedOutcome = $"Would execute {actionType} with {parameters.Count} parameters",
                AffectedTargets = parameters.Values
                    .Where(v => v != null)
                    .Select(v => v.ToString()!)
                    .ToList()
            };

            Logger.Information($"[SANDBOX] Simulated: {result.ActionDescription} | Risk: {risk}");
            return result;
        }

        private void LoadWhitelist()
        {
            try
            {
                if (File.Exists(_whitelistPath))
                {
                    var json = File.ReadAllText(_whitelistPath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
                    if (data != null)
                    {
                        foreach (var kvp in data)
                        {
                            _whitelist[kvp.Key] = new HashSet<string>(kvp.Value, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SafetyGuardian] Failed to load whitelist");
            }
        }

        private void SaveWhitelist()
        {
            try
            {
                var data = _whitelist.ToDictionary(k => k.Key, v => v.Value.ToList());
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_whitelistPath, json);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SafetyGuardian] Failed to save whitelist");
            }
        }
    }
}
