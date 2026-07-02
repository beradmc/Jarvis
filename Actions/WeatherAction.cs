using System;
using System.Net.Http;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Actions
{
    public class WeatherAction : IAction
    {
        public string Name => "get_weather";
        public string Description => "Gets the current weather for a location using wttr.in.";

        public async Task<string> ExecuteAsync(string payload)
        {
            var location = string.IsNullOrWhiteSpace(payload) ? "Istanbul" : payload.Trim();
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                client.DefaultRequestHeaders.Add("User-Agent", "JARVIS-Windows/1.0");
                // format=3 → "Istanbul: ⛅️ +18°C" gibi kısa çıktı
                var result = await client.GetStringAsync(
                    $"https://wttr.in/{Uri.EscapeDataString(location)}?format=3&lang=tr");
                var trimmed = result.Trim();
                Logger.Information($"[Weather] {trimmed}");
                return trimmed;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get weather");
                return $"Hata: Hava durumu alınamadı — {ex.Message}";
            }
        }
    }
}
