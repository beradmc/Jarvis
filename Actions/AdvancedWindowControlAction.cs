using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JarvisCSharp.Services.Automation;
using System.Drawing; // For Point

namespace JarvisCSharp.Actions
{
    public class AdvancedWindowControlAction : IAction
    {
        public string Name => "advanced_window_control";
        public string Description => "Handles advanced window, UI, and vision automation tasks.";

        private readonly AutomationControllerService _automation;
        private readonly VisionEngineService _vision;
        private readonly OCRService _ocr;
        private readonly WorkflowExecutorService _workflow;
        private readonly LearningSystemService _learning;
        private readonly ActionLog _actionLog;

        public AdvancedWindowControlAction(
            AutomationControllerService automation,
            VisionEngineService vision,
            OCRService ocr,
            WorkflowExecutorService workflow,
            LearningSystemService learning,
            ActionLog actionLog)
        {
            _automation = automation;
            _vision = vision;
            _ocr = ocr;
            _workflow = workflow;
            _learning = learning;
            _actionLog = actionLog;
        }

        public async Task<string> ExecuteAsync(string payload)
        {
            try
            {
                var data = JsonSerializer.Deserialize<JsonElement>(payload);
                var subAction = data.GetProperty("sub_action").GetString();

                switch (subAction)
                {
                    case "analyze_screen":
                        var query = GetString(data, "query", "Ekranda ne var?");
                        var targetStr = GetString(data, "target", "ActiveWindow");
                        
                        var visionResult = await _vision.AnalyzeTargetAsync(query, targetStr);
                        return $"Analiz tamamlandı.\nBulunan öğeler: {visionResult.Elements.Count}\nAçıklama: {visionResult.AnalysisText}";

                    case "click_element":
                        var elementName = GetString(data, "elementName", "");
                        var clickTypeStr = GetString(data, "clickType", "LeftSingle");
                        Enum.TryParse<ClickType>(clickTypeStr, true, out var clickType);
                        
                        if (string.IsNullOrWhiteSpace(elementName)) 
                            return "Hata: elementName boş olamaz.";

                        var visionOptions = new VisionOptions { MinConfidenceScore = 70 };
                        var vResult = await _vision.AnalyzeTargetAsync($"Find the element '{elementName}'. If there are multiple, list them all.", "ActiveWindow", visionOptions);
                        
                        if (vResult.Elements.Count == 0)
                        {
                            return $"Öğe bulunamadı: '{elementName}'. Lütfen 'analyze' ile ekranı inceleyin.";
                        }
                        else if (vResult.Elements.Count == 1)
                        {
                            var element = vResult.Elements[0];
                            var clickRes = await _automation.ClickAsync(element.AbsoluteCoordinate, clickType);
                            return clickRes.Success ? $"'{elementName}' öğesine tıklandı." : $"Tıklama hatası: {clickRes.Message}";
                        }
                        else
                        {
                            var options = string.Join("\n", vResult.Elements.Select((e, i) => $"{i + 1}. {e.Description} (Koordinat: {e.AbsoluteCoordinate.X},{e.AbsoluteCoordinate.Y})"));
                            return $"AMBIGUITY_DETECTED: Ekranda birden fazla '{elementName}' benzeri öğe bulundu. Lütfen kullanıcıya doğrudan sorarak hangisine tıklanacağını belirle:\n{options}";
                        }

                    case "type_text":
                        var text = GetString(data, "text", "");
                        var clearExisting = GetBool(data, "clearExisting", false);
                        var typeResult = await _automation.TypeTextAsync(text, null, clearExisting);
                        return typeResult.Success ? "Metin yazıldı." : $"Hata: {typeResult.Message}";

                    case "send_shortcut":
                        var shortcut = GetString(data, "shortcut", "");
                        var shortResult = await _automation.SendShortcutAsync(shortcut);
                        return shortResult.Success ? "Kısayol gönderildi." : $"Hata: {shortResult.Message}";

                    case "multi_step_workflow":
                        var command = GetString(data, "command", "");
                        var wf = await _workflow.ParseWorkflowAsync(command);
                        var execResult = await _workflow.ExecuteWorkflowAsync(wf);
                        return $"İş akışı tamamlandı. Başarılı: {execResult.Success}, Mesaj: {execResult.Message}";

                    case "extract_text":
                        var lang = GetString(data, "language", "tr");
                        return "extract_text çağrıldı.";

                    case "launch_app":
                        var appName = GetString(data, "appName", "");
                        var launchRes = await _automation.LaunchApplicationAsync(appName);
                        return launchRes.Success ? "Uygulama başlatıldı." : $"Hata: {launchRes.Message}";

                    case "switch_window":
                        var winTitle = GetString(data, "windowTitle", "");
                        var handle = GetWindowHandle(winTitle);
                        if (handle == IntPtr.Zero) return "Pencere bulunamadı.";
                        var focusRes = await _automation.BringWindowToFocusAsync(handle);
                        return focusRes.Success ? "Pencere odaklandı." : $"Hata: {focusRes.Message}";

                    case "maximize_window":
                        var handleMax = GetActiveWindowHandle();
                        if (handleMax == IntPtr.Zero) return "Aktif pencere yok.";
                        var maxRes = await _automation.MaximizeWindowAsync(handleMax);
                        return maxRes.Success ? "Pencere büyütüldü." : $"Hata: {maxRes.Message}";

                    case "minimize_window":
                        var handleMin = GetActiveWindowHandle();
                        if (handleMin == IntPtr.Zero) return "Aktif pencere yok.";
                        var minRes = await _automation.MinimizeWindowAsync(handleMin);
                        return minRes.Success ? "Pencere küçültüldü." : $"Hata: {minRes.Message}";

                    case "close_window":
                        var handleClose = GetActiveWindowHandle();
                        if (handleClose == IntPtr.Zero) return "Aktif pencere yok.";
                        var closeRes = await _automation.CloseWindowAsync(handleClose);
                        return closeRes.Success ? "Pencere kapatıldı." : $"Hata: {closeRes.Message}";

                    case "move_window":
                        return "Move window requires WindowPosition enum, not fully implemented here.";

                    case "correct_detection":
                        var cAppName = GetString(data, "appName", "");
                        var cElementDesc = GetString(data, "elementName", "");
                        var cCorrectLoc = GetString(data, "correctLocation", "");
                        _learning.StoreCorrection(cAppName, cElementDesc, cCorrectLoc, null);
                        return "Düzeltme kaydedildi ve öğrenildi.";

                    case "learn_alias":
                        var alias = GetString(data, "alias", "");
                        var canonical = GetString(data, "canonicalName", "");
                        var aliasApp = GetString(data, "appName", "");
                        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(canonical))
                            return "Hata: alias ve canonicalName boş olamaz.";
                        
                        _learning.AddAlias(alias, canonical, string.IsNullOrWhiteSpace(aliasApp) ? null : aliasApp);
                        return $"'{alias}' ifadesi '{canonical}' olarak öğrenildi.";

                    default:
                        return $"Bilinmeyen alt eylem: {subAction}";
                }
            }
            catch (Exception ex)
            {
                return $"Hata: {ex.Message}";
            }
        }

        private IntPtr GetWindowHandle(string title)
        {
            var proc = Process.GetProcesses().FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle) && p.MainWindowTitle.Contains(title, StringComparison.OrdinalIgnoreCase));
            return proc?.MainWindowHandle ?? IntPtr.Zero;
        }

        private IntPtr GetActiveWindowHandle()
        {
            // For now, return a dummy zero or find first UI app. (You would use GetForegroundWindow from user32.dll normally)
            return IntPtr.Zero; 
        }

        private string GetString(JsonElement data, string key, string def)
        {
            if (data.TryGetProperty(key, out var val))
                return val.GetString() ?? def;
            return def;
        }

        private bool GetBool(JsonElement data, string key, bool def)
        {
            if (data.TryGetProperty(key, out var val))
            {
                if (val.ValueKind == JsonValueKind.True) return true;
                if (val.ValueKind == JsonValueKind.False) return false;
            }
            return def;
        }

        private int GetInt(JsonElement data, string key, int def)
        {
            if (data.TryGetProperty(key, out var val) && val.TryGetInt32(out var res))
                return res;
            return def;
        }
    }
}
