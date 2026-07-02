using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using JarvisCSharp.Config;
using JarvisCSharp.Core;

namespace JarvisCSharp.Actions
{
    public class YoutubeStatsAction : IAction
    {
        public string Name => "youtube_stats";
        public string Description => "YouTube kanalı istatistiklerini YouTube Data API ile çeker.";

        private const string ApiRoot = "https://www.googleapis.com/youtube/v3";
        private const int Timeout = 14;

        public async Task<string> ExecuteAsync(string payload)
        {
            try
            {
                string query = "overview", handle = "", videoLimit = "6";
                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("query",       out var q))  query      = q.GetString() ?? query;
                    if (root.TryGetProperty("handle",      out var h))  handle     = h.GetString() ?? handle;
                    if (root.TryGetProperty("video_limit", out var vl)) videoLimit = vl.GetRawText();
                }
                catch { }

                int limit  = int.TryParse(videoLimit, out var lv) ? lv : 6;
                var result = await GetYoutubeReport(query, handle, limit);
                Logger.Information($"[YouTube] {result[..Math.Min(150, result.Length)]}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "YoutubeStatsAction failed");
                return $"Hata: YouTube raporu alınamadı — {ex.Message}";
            }
        }

        public static async Task<string> GetYoutubeReport(string query, string handle, int videoLimit = 6)
        {
            var apiKey = AppConfig.GetValue("youtube_api_key", "");
            if (string.IsNullOrWhiteSpace(apiKey))
                return "YouTube istatistikleri için YouTube API Key gerekli. Ayarlar > api_keys.json dosyasına youtube_api_key ekle.";

            var channelHandle = string.IsNullOrWhiteSpace(handle)
                ? AppConfig.GetValue("youtube_channel_handle", "")
                : handle;

            if (string.IsNullOrWhiteSpace(channelHandle))
                return "YouTube kanal handle'ı ayarlanmamış. api_keys.json dosyasına youtube_channel_handle ekle.";

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(Timeout) };
                client.DefaultRequestHeaders.Add("User-Agent", "JARVIS-Windows/1.0");

                // 1) Kanal bilgilerini çek
                var filterParam = channelHandle.StartsWith("@") ? "forHandle" : "id";
                var channelUrl = $"{ApiRoot}/channels?part=snippet,statistics,contentDetails&{filterParam}={Uri.EscapeDataString(channelHandle)}&key={apiKey}";
                var channelJson = await client.GetStringAsync(channelUrl);
                using var channelDoc = JsonDocument.Parse(channelJson);
                var items = channelDoc.RootElement.GetProperty("items");
                if (items.GetArrayLength() == 0)
                    return $"YouTube kanalı bulunamadı: {channelHandle}";

                var channel    = items[0];
                var snippet    = channel.GetProperty("snippet");
                var statistics = channel.GetProperty("statistics");
                var uploadsId  = channel.GetProperty("contentDetails")
                                        .GetProperty("relatedPlaylists")
                                        .GetProperty("uploads").GetString() ?? "";

                var channelTitle = snippet.GetProperty("title").GetString() ?? "Kanal";
                var subscribers  = ParseLong(statistics, "subscriberCount");
                var totalViews   = ParseLong(statistics, "viewCount");
                var videoCount   = ParseLong(statistics, "videoCount");

                var parts = new List<string>
                {
                    $"Public YouTube verisine göre {channelTitle} kanalında {FmtInt(subscribers)} abone, " +
                    $"{FmtInt(totalViews)} toplam görüntülenme ve {FmtInt(videoCount)} video var."
                };

                // 2) Son videoları çek
                if (!string.IsNullOrEmpty(uploadsId))
                {
                    var plUrl = $"{ApiRoot}/playlistItems?part=snippet,contentDetails&playlistId={uploadsId}&maxResults={Math.Min(10, videoLimit)}&key={apiKey}";
                    var plJson = await client.GetStringAsync(plUrl);
                    using var plDoc = JsonDocument.Parse(plJson);
                    var plItems = plDoc.RootElement.GetProperty("items");

                    var videoIds = new List<string>();
                    foreach (var item in plItems.EnumerateArray())
                    {
                        var vid = item.TryGetProperty("contentDetails", out var cd) &&
                                  cd.TryGetProperty("videoId", out var vidId)
                                  ? vidId.GetString() ?? ""
                                  : "";
                        if (!string.IsNullOrEmpty(vid)) videoIds.Add(vid);
                    }

                    if (videoIds.Any())
                    {
                        var vUrl = $"{ApiRoot}/videos?part=snippet,statistics&id={string.Join(",", videoIds)}&key={apiKey}";
                        var vJson = await client.GetStringAsync(vUrl);
                        using var vDoc = JsonDocument.Parse(vJson);
                        var videos = vDoc.RootElement.GetProperty("items").EnumerateArray().ToList();

                        if (videos.Any())
                        {
                            var avgViews  = videos.Average(v => ParseLong(v.GetProperty("statistics"), "viewCount"));
                            var avgLikes  = videos.Average(v => ParseLong(v.GetProperty("statistics"), "likeCount"));
                            var avgCom    = videos.Average(v => ParseLong(v.GetProperty("statistics"), "commentCount"));
                            parts.Add($"Son {videos.Count} videonun ortalaması {FmtInt((long)avgViews)} izlenme, " +
                                      $"{FmtInt((long)avgLikes)} beğeni ve {FmtInt((long)avgCom)} yorum.");

                            var best = videos.OrderByDescending(v => ParseLong(v.GetProperty("statistics"), "viewCount")).First();
                            var bestTitle = best.GetProperty("snippet").GetProperty("title").GetString() ?? "Video";
                            var bestViews = ParseLong(best.GetProperty("statistics"), "viewCount");
                            parts.Add($"En güçlü son video '{bestTitle}' - {FmtInt(bestViews)} izlenme.");

                            if (query.ToLower().Contains("detay") || query.ToLower().Contains("analiz"))
                            {
                                var details = videos.Take(3).Select((v, i) =>
                                {
                                    var vTitle = v.GetProperty("snippet").GetProperty("title").GetString() ?? "";
                                    var views  = ParseLong(v.GetProperty("statistics"), "viewCount");
                                    var likes  = ParseLong(v.GetProperty("statistics"), "likeCount");
                                    return $"{i + 1}. {vTitle} - {FmtInt(views)} izlenme, {FmtInt(likes)} beğeni";
                                });
                                parts.Add("Son video detayı: " + string.Join(" | ", details) + ".");
                            }
                        }
                    }
                }

                parts.Add("Not: Studio erişimi olmadan izlenme süresi, CTR ve gelir verilerini göremem.");
                return string.Join(" ", parts);
            }
            catch (Exception ex)
            {
                return $"YouTube istatistikleri alınamadı: {ex.Message}";
            }
        }

        private static long ParseLong(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var v)) return 0;
            return long.TryParse(v.GetString(), out var l) ? l : 0;
        }

        private static string FmtInt(long v) => $"{v:N0}".Replace(",", ".");
    }
}
