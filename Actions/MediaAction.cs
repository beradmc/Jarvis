using System;
using System.Threading.Tasks;
using JarvisCSharp.Utils;

namespace JarvisCSharp.Actions
{
    public class MediaAction : IAction
    {
        public string Name => "play_media";
        public string Description => "Plays media on YouTube or Spotify.";

        public Task<string> ExecuteAsync(string payload)
        {
            try
            {
                if (payload.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
                {
                    var query = payload[8..].Trim();
                    WinHelpers.OpenUrl($"spotify:search:{Uri.EscapeDataString(query)}");
                    return Task.FromResult($"Spotify'da açıldı: {query}");
                }

                // YouTube search
                var ytQuery = Uri.EscapeDataString(payload);
                WinHelpers.OpenUrl($"https://www.youtube.com/results?search_query={ytQuery}");
                return Task.FromResult($"YouTube'da arandı: {payload}");
            }
            catch (Exception ex)
            {
                return Task.FromResult($"Hata: Medya açılamadı — {ex.Message}");
            }
        }
    }
}
