using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using JarvisCSharp.Config;
using JarvisCSharp.Core;

namespace JarvisCSharp.Actions
{
    public class ClipboardAction : IAction
    {
        public string Name => "clipboard_action";
        public string Description => "Reads or writes to the clipboard. Supports smart AI processing.";

        public async Task<string> ExecuteAsync(string payload)
        {
            var parts = payload.Split(':', 2);
            var action = parts[0].Trim().ToLower();
            var extra  = parts.Length > 1 ? parts[1].Trim() : "";

            try
            {
                return action switch
                {
                    "read"  => ReadClipboard(),
                    "write" => WriteClipboard(extra),
                    "smart" => await SmartClipboard(extra),
                    _       => "Bilinmeyen pano eylemi."
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Clipboard action failed");
                return $"Hata: Pano işlemi başarısız — {ex.Message}";
            }
        }

        private static string ReadClipboard()
        {
            string content = "";
            Application.Current.Dispatcher.Invoke(() =>
            {
                content = Clipboard.GetText();
            });
            if (string.IsNullOrWhiteSpace(content))
                return "Panoda metin yok.";
            Logger.Information($"[Clipboard] Read {content.Length} chars");
            return $"Panodaki metin: {content}";
        }

        private static string WriteClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return "Yazılacak metin boş.";
            Application.Current.Dispatcher.Invoke(() =>
            {
                Clipboard.SetText(text);
            });
            Logger.Information($"[Clipboard] Written {text.Length} chars");
            return "Metin panoya yazıldı.";
        }

        private static async Task<string> SmartClipboard(string instruction)
        {
            string clipContent = "";
            Application.Current.Dispatcher.Invoke(() =>
            {
                clipContent = Clipboard.GetText();
            });

            if (string.IsNullOrWhiteSpace(clipContent))
                return "Panoda işlenecek metin yok.";

            var apiKey = AppConfig.GetValue("gemini_api_key", "");
            if (string.IsNullOrEmpty(apiKey))
                return "Gemini API key eksik.";

            var prompt = string.IsNullOrWhiteSpace(instruction)
                ? $"Şu metni analiz et ve özetle:\n\n{clipContent}"
                : $"{instruction}:\n\n{clipContent}";

            try
            {
                var body = JsonSerializer.Serialize(new
                {
                    contents = new[] { new { parts = new[] { new { text = prompt } } } }
                });

                using var client  = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var content       = new StringContent(body, Encoding.UTF8, "application/json");
                var url           = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={apiKey}";
                var response      = await client.PostAsync(url, content);
                var respJson      = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(respJson);
                var result = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text").GetString() ?? "İşlem tamamlandı.";

                // Sonucu panoya da yaz
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Clipboard.SetText(result);
                });

                Logger.Information($"[Clipboard Smart] Done: {result[..Math.Min(80, result.Length)]}");
                return result;
            }
            catch (Exception ex)
            {
                return $"Hata: AI işlemi başarısız — {ex.Message}";
            }
        }
    }
}
