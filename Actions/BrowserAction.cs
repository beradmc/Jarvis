using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JarvisCSharp.Utils;

namespace JarvisCSharp.Actions
{
    public class BrowserAction : IAction
    {
        public string Name => "browser_control";
        public string Description => "Opens URLs, searches, or plays YouTube videos directly.";

        public async Task<string> ExecuteAsync(string payload)
        {
            try
            {
                if (payload.StartsWith("open_url:", StringComparison.OrdinalIgnoreCase))
                {
                    var url = payload[9..].Trim();
                    WinHelpers.OpenUrl(url);
                    return $"URL açıldı: {url}";
                }

                if (payload.StartsWith("play_youtube:", StringComparison.OrdinalIgnoreCase))
                {
                    var query = payload[13..].Trim();
                    var videoUrl = await FindFirstYoutubeVideo(query);
                    WinHelpers.OpenUrl(videoUrl);
                    return $"YouTube'da oynatılıyor: {query}";
                }

                if (payload.StartsWith("search:", StringComparison.OrdinalIgnoreCase))
                {
                    var query = payload[7..].Trim();
                    WinHelpers.OpenUrl($"https://www.google.com/search?q={Uri.EscapeDataString(query)}");
                    return $"Google'da arandı: {query}";
                }

                // Default: search
                WinHelpers.OpenUrl($"https://www.google.com/search?q={Uri.EscapeDataString(payload)}");
                return $"Tarayıcıda arandı: {payload}";
            }
            catch (Exception ex)
            {
                return $"Hata: Tarayıcı açılamadı — {ex.Message}";
            }
        }

        /// <summary>
        /// YouTube Data API olmadan, YouTube'un önerdiği ilk videoyu InnerTube API ile bulur.
        /// </summary>
        private static async Task<string> FindFirstYoutubeVideo(string query)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");

                var searchUrl = $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
                var html = await client.GetStringAsync(searchUrl);

                // ytInitialData içinde videoId bul
                var match = Regex.Match(html, @"""videoId"":""([a-zA-Z0-9_-]{11})""");
                if (match.Success)
                    return $"https://www.youtube.com/watch?v={match.Groups[1].Value}";
            }
            catch { }

            // Fallback: search sayfası
            return $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
        }
    }
}
