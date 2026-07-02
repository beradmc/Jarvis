using System;
using System.Text.Json;
using System.Threading.Tasks;
using JarvisCSharp.Core;
using JarvisCSharp.Services;

namespace JarvisCSharp.Actions
{
    public class RemindersAction : IAction
    {
        private readonly OutlookComService _outlookService;

        public string Name => "reminders_action";
        public string Description => "Outlook görevlerini/hatırlatıcılarını yönetir.";

        public RemindersAction()
        {
            _outlookService = new OutlookComService();
        }

        public Task<string> ExecuteAsync(string payload)
        {
            try
            {
                if (payload.StartsWith("add:", StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(AddReminder(payload[4..]));
                else
                {
                    var query = payload.StartsWith("get:", StringComparison.OrdinalIgnoreCase)
                        ? payload[4..].Split(':')[0] : "upcoming";
                    return Task.FromResult(GetReminders(query));
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "RemindersAction failed");
                return Task.FromResult($"Hata: Hatırlatıcı işlemi başarısız — {ex.Message}");
            }
        }

        private string GetReminders(string query)
        {
            return _outlookService.GetReminders(query);
        }

        private string AddReminder(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var dueIso = root.TryGetProperty("due_iso", out var d) ? d.GetString() ?? "" : "";
                var notes = root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "";
                var listName = root.TryGetProperty("list_name", out var l) ? l.GetString() ?? "" : "";
                var priority = root.TryGetProperty("priority", out var p) ? p.GetString() ?? "" : "";
                var allDay = root.TryGetProperty("all_day", out var a) && a.GetBoolean();

                return _outlookService.AddReminder(
                    title,
                    dueIso,
                    notes,
                    listName,
                    priority,
                    allDay
                );
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Reminder add failed");
                return $"Hata: Hatırlatıcı eklenemedi — {ex.Message}";
            }
        }
    }
}
