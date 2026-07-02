using System;
using System.Threading.Tasks;
using JarvisCSharp.Core;
using JarvisCSharp.Services;
using Microsoft.Extensions.Logging;

namespace JarvisCSharp.Actions
{
    public class UiControlAction : IAction
    {
        public string Name => "ui_control";
        public string Description => "Akıllı Arayüz Otomasyonu: Aktif penceredeki öğeleri bulur, tıklar veya metin yazar (Koordinatsız).";

        private readonly UIAutomationService _uiService;

        public UiControlAction(UIAutomationService uiService)
        {
            _uiService = uiService;
        }

        public async Task<string> ExecuteAsync(string payload)
        {
            string actualPayload = payload;
            try
            {
                if (payload.TrimStart().StartsWith("{"))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(payload);
                    if (doc.RootElement.TryGetProperty("payload", out var prop))
                    {
                        actualPayload = prop.GetString() ?? "";
                    }
                }
            }
            catch { }

            var parts = actualPayload.Split(':', 2);
            var action = parts[0].Trim().ToLower();
            var value = parts.Length > 1 ? parts[1].Trim() : "";

            try
            {
                var result = action switch
                {
                    "analyze" => await _uiService.AnalyzeActiveWindowAsync(),
                    "click" => await _uiService.ClickElementByNameAsync(value),
                    "type" => await HandleTypeAction(value),
                    _ => $"Bilinmeyen UI eylemi: {action}"
                };

                Logger.Information($"[UI Control] {action} → {result.Split('\n')[0]}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"UI action failed: {action}");
                return $"Hata: {action} başarısız — {ex.Message}";
            }
        }

        private async Task<string> HandleTypeAction(string value)
        {
            // Format: "ElementName,Text to type"
            var commaIndex = value.IndexOf(',');
            if (commaIndex == -1) return "Geçersiz format. Lütfen 'ÖğeAdı,Metin' şeklinde gönderin.";

            var elementName = value.Substring(0, commaIndex).Trim();
            var text = value.Substring(commaIndex + 1).Trim();

            return await _uiService.TypeTextIntoElementAsync(elementName, text);
        }
    }
}
