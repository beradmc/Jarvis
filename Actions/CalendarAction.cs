using System;
using System.Text.Json;
using System.Threading.Tasks;
using JarvisCSharp.Core;
using JarvisCSharp.Services;

namespace JarvisCSharp.Actions
{
    public class CalendarAction : IAction
    {
        private readonly OutlookComService _outlookService;

        public string Name => "calendar_action";
        public string Description => "Outlook takviminde etkinlik okur, ekler veya siler.";

        public CalendarAction()
        {
            _outlookService = new OutlookComService();
        }

        public Task<string> ExecuteAsync(string payload)
        {
            try
            {
                if (payload.StartsWith("add:", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(AddEvent(payload[4..]));
                else if (payload.StartsWith("delete:", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(DeleteEvent(payload[7..]));
                else
                {
                    var query = payload.StartsWith("get:", StringComparison.OrdinalIgnoreCase)
                        ? payload[4..].Split(':')[0] : "today";
                    return Task.FromResult(GetEvents(query));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "CalendarAction failed");
                return Task.FromResult($"Hata: Takvim işlemi başarısız — {ex.Message}");
            }
        }

        private string GetEvents(string query)
        {
            return _outlookService.GetCalendarEvents(query);
        }

        private string AddEvent(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var startIso = root.TryGetProperty("start_iso", out var s) ? s.GetString() ?? "" : "";
                var endIso = root.TryGetProperty("end_iso", out var e) ? e.GetString() ?? "" : "";
                var location = root.TryGetProperty("location", out var l) ? l.GetString() ?? "" : "";
                var notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
                var calendarName = root.TryGetProperty("calendar_name", out var c) ? c.GetString() ?? "" : "";
                var allDay = root.TryGetProperty("all_day", out var a) && a.GetBoolean();

                return _outlookService.AddCalendarEvent(
                    title,
                    startIso,
                    endIso,
                    location,
                    notes,
                    calendarName,
                    allDay
                );
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Calendar add event failed");
                return $"Hata: Etkinlik eklenemedi — {ex.Message}";
            }
        }

        private string DeleteEvent(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var startIso = root.TryGetProperty("start_iso", out var s) ? s.GetString() ?? "" : "";
                var deleteAll = root.TryGetProperty("delete_all_matches", out var d) && d.GetBoolean();

                return _outlookService.DeleteCalendarEvent(title, startIso, deleteAll);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Calendar delete event failed");
                return $"Hata: Etkinlik silinemedi — {ex.Message}";
            }
        }
    }
}
