using System;
using System.Runtime.InteropServices;
using System.Text;
using JarvisCSharp.Core;
using JarvisCSharp.Utils;

namespace JarvisCSharp.Services
{
    /// <summary>
    /// Provides COM automation for Microsoft Outlook calendar and tasks.
    /// Falls back to web interfaces (Google Calendar, Microsoft To-Do) when Outlook is not available.
    /// </summary>
    public class OutlookComService
    {
        private dynamic? _outlookApp;
        private bool _outlookAvailable;

        public OutlookComService()
        {
            try
            {
                _outlookApp = GetOutlookApplication();
                _outlookAvailable = _outlookApp != null;
                Logger.Information($"OutlookComService initialized (Outlook available: {_outlookAvailable})");
            }
            catch (Exception ex)
            {
                Logger.Warning("Outlook COM initialization failed, will use web fallback: {Message}", ex.Message);
                _outlookAvailable = false;
            }
        }

        /// <summary>
        /// Retrieves calendar events from Outlook or falls back to Google Calendar web.
        /// Supports query types: "today", "tomorrow", "week", "next"
        /// </summary>
        public string GetCalendarEvents(string query = "today", int limit = 6)
        {
            if (!_outlookAvailable || _outlookApp == null)
            {
                Logger.Information("[Calendar] Outlook not available, opening Google Calendar");
                return FallbackToWeb("https://calendar.google.com", query);
            }

            try
            {
                var ns = _outlookApp!.GetNamespace("MAPI");
                var calendarFolder = ns.GetDefaultFolder(9); // olFolderCalendar = 9
                var items = calendarFolder.Items;
                items.Sort("[Start]");
                items.IncludeRecurrences = true;

                var (startDate, endDate) = GetDateRange(query);
                var filter = $"[Start] >= '{startDate:g}' AND [Start] <= '{endDate:g}'";
                var filteredItems = items.Restrict(filter);

                var sb = new StringBuilder();
                sb.AppendLine($"Takvim etkinlikleri ({query}):");

                int count = 0;
                foreach (dynamic item in filteredItems)
                {
                    if (count >= limit) break;

                    try
                    {
                        var subject = item.Subject ?? "Başlıksız";
                        var start = (DateTime)item.Start;
                        var end = (DateTime)item.End;
                        var location = item.Location ?? "";
                        var isAllDay = item.AllDayEvent;

                        if (isAllDay)
                        {
                            sb.AppendLine($"• {start:dd.MM.yyyy} (Tüm gün) - {subject}");
                        }
                        else
                        {
                            sb.AppendLine($"• {start:dd.MM.yyyy HH:mm}-{end:HH:mm} - {subject}");
                        }

                        if (!string.IsNullOrEmpty(location))
                        {
                            sb.AppendLine($"  Konum: {location}");
                        }

                        count++;
                    }
                    finally
                    {
                        if (item != null) Marshal.ReleaseComObject(item);
                    }
                }

                if (count == 0)
                {
                    sb.AppendLine($"({query} için etkinlik bulunamadı)");
                }

                Marshal.ReleaseComObject(items);
                Marshal.ReleaseComObject(calendarFolder);
                Marshal.ReleaseComObject(ns);

                Logger.Information($"[Calendar] Retrieved {count} events for query: {query}");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Calendar] Failed to retrieve events, falling back to web");
                return FallbackToWeb("https://calendar.google.com", query);
            }
        }

        /// <summary>
        /// Creates a calendar event in Outlook or falls back to Google Calendar web.
        /// </summary>
        public string AddCalendarEvent(
            string title,
            string startIso,
            string endIso = "",
            string location = "",
            string notes = "",
            string calendarName = "",
            bool allDay = false)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "Hata: Etkinlik başlığı boş olamaz.";
            }

            if (string.IsNullOrWhiteSpace(startIso))
            {
                return "Hata: Başlangıç tarihi/saati gerekli (start_iso).";
            }

            if (!DateTime.TryParse(startIso, out var startDateTime))
            {
                return $"Hata: Başlangıç tarihi anlaşılamadı: {startIso}";
            }

            if (!_outlookAvailable || _outlookApp == null)
            {
                Logger.Information("[Calendar] Outlook not available, opening Google Calendar for add");
                return FallbackToGoogleCalendarAdd(title, startIso, endIso, location, notes, allDay);
            }

            try
            {
                var ns = _outlookApp!.GetNamespace("MAPI");
                var calendarFolder = ns.GetDefaultFolder(9); // olFolderCalendar = 9
                dynamic appointment = _outlookApp!.CreateItem(1); // olAppointmentItem = 1

                appointment.Subject = title;
                appointment.Start = startDateTime;

                if (!string.IsNullOrWhiteSpace(endIso) && DateTime.TryParse(endIso, out var endDateTime))
                {
                    appointment.End = endDateTime;
                }
                else
                {
                    appointment.End = startDateTime.AddHours(1);
                }

                if (!string.IsNullOrEmpty(location))
                {
                    appointment.Location = location;
                }

                if (!string.IsNullOrEmpty(notes))
                {
                    appointment.Body = notes;
                }

                appointment.AllDayEvent = allDay;
                appointment.ReminderSet = true;
                appointment.ReminderMinutesBeforeStart = 15;

                appointment.Save();

                Marshal.ReleaseComObject(appointment);
                Marshal.ReleaseComObject(calendarFolder);
                Marshal.ReleaseComObject(ns);

                var whenStr = allDay
                    ? startDateTime.ToString("dd.MM.yyyy")
                    : startDateTime.ToString("dd.MM.yyyy HH:mm");

                Logger.Information($"[Calendar] Event created: {title} at {whenStr}");
                return $"Outlook'ta '{title}' etkinliği oluşturuldu ({whenStr}).";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Calendar] Failed to create event, falling back to web");
                return FallbackToGoogleCalendarAdd(title, startIso, endIso, location, notes, allDay);
            }
        }

        /// <summary>
        /// Deletes a calendar event from Outlook or opens Google Calendar for manual deletion.
        /// </summary>
        public string DeleteCalendarEvent(string title, string startIso = "", bool deleteAllMatches = false)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "Hata: Silinecek etkinlik başlığı gerekli.";
            }

            if (!_outlookAvailable || _outlookApp == null)
            {
                Logger.Information("[Calendar] Outlook not available, opening Google Calendar for delete");
                WinHelpers.OpenUrl("https://calendar.google.com");
                return $"Google Calendar açıldı. '{title}' etkinliğini tarayıcıdan silebilirsin.";
            }

            try
            {
                var ns = _outlookApp!.GetNamespace("MAPI");
                var calendarFolder = ns.GetDefaultFolder(9); // olFolderCalendar = 9
                var items = calendarFolder.Items;

                string filter = $"[Subject] = '{title.Replace("'", "''")}'";

                if (!string.IsNullOrWhiteSpace(startIso) && DateTime.TryParse(startIso, out var startDateTime))
                {
                    filter += $" AND [Start] >= '{startDateTime:g}' AND [Start] <= '{startDateTime.AddMinutes(1):g}'";
                }

                var filteredItems = items.Restrict(filter);
                int deletedCount = 0;

                foreach (dynamic item in filteredItems)
                {
                    try
                    {
                        item.Delete();
                        deletedCount++;

                        if (!deleteAllMatches)
                        {
                            Marshal.ReleaseComObject(item);
                            break;
                        }
                    }
                    finally
                    {
                        if (item != null) Marshal.ReleaseComObject(item);
                    }
                }

                Marshal.ReleaseComObject(filteredItems);
                Marshal.ReleaseComObject(items);
                Marshal.ReleaseComObject(calendarFolder);
                Marshal.ReleaseComObject(ns);

                if (deletedCount == 0)
                {
                    Logger.Warning($"[Calendar] No matching event found for deletion: {title}");
                    return $"'{title}' etkinliği bulunamadı. Tarih bilgisi ekleyerek tekrar dene.";
                }

                Logger.Information($"[Calendar] Deleted {deletedCount} event(s): {title}");
                return $"'{title}' etkinliği silindi ({deletedCount} etkinlik).";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Calendar] Failed to delete event, opening web for manual deletion");
                WinHelpers.OpenUrl("https://calendar.google.com");
                return $"Silme işlemi başarısız oldu. Google Calendar açıldı, '{title}' etkinliğini tarayıcıdan silebilirsin.";
            }
        }

        /// <summary>
        /// Retrieves reminders/tasks from Outlook or falls back to Microsoft To-Do web.
        /// Supports query types: "upcoming", "today", "overdue"
        /// </summary>
        public string GetReminders(string query = "upcoming", int limit = 8, string listName = "")
        {
            if (!_outlookAvailable || _outlookApp == null)
            {
                Logger.Information("[Reminders] Outlook not available, opening Microsoft To-Do");
                WinHelpers.OpenUrl("https://to-do.microsoft.com/tasks");
                var hint = query switch
                {
                    "today" => "bugün",
                    "overdue" => "gecikmiş",
                    _ => "yaklaşan"
                };
                return $"Microsoft To-Do web açıldı ({hint} görevler). Liste: {listName}";
            }

            try
            {
                var ns = _outlookApp!.GetNamespace("MAPI");
                var tasksFolder = ns.GetDefaultFolder(13); // olFolderTasks = 13
                var items = tasksFolder.Items;
                items.Sort("[DueDate]");

                var filter = BuildTaskFilter(query);
                var filteredItems = string.IsNullOrEmpty(filter) ? items : items.Restrict(filter);

                var sb = new StringBuilder();
                sb.AppendLine($"Hatırlatıcılar ({query}):");

                int count = 0;
                foreach (dynamic task in filteredItems)
                {
                    if (count >= limit) break;

                    try
                    {
                        var subject = task.Subject ?? "Başlıksız";
                        var complete = task.Complete;

                        if (complete && query != "overdue") continue;

                        var dueDate = task.DueDate;
                        var hasDueDate = dueDate != null && dueDate != DateTime.MinValue;

                        if (hasDueDate)
                        {
                            var due = (DateTime)dueDate;
                            sb.AppendLine($"• {due:dd.MM.yyyy HH:mm} - {subject}");
                        }
                        else
                        {
                            sb.AppendLine($"• {subject}");
                        }

                        if (complete)
                        {
                            sb.AppendLine("  (Tamamlandı)");
                        }

                        count++;
                    }
                    finally
                    {
                        if (task != null) Marshal.ReleaseComObject(task);
                    }
                }

                if (count == 0)
                {
                    sb.AppendLine($"({query} için görev bulunamadı)");
                }

                Marshal.ReleaseComObject(filteredItems);
                Marshal.ReleaseComObject(items);
                Marshal.ReleaseComObject(tasksFolder);
                Marshal.ReleaseComObject(ns);

                Logger.Information($"[Reminders] Retrieved {count} tasks for query: {query}");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Reminders] Failed to retrieve tasks, falling back to web");
                WinHelpers.OpenUrl("https://to-do.microsoft.com/tasks");
                return $"Microsoft To-Do web açıldı. Outlook görevleri okunamadı: {ex.Message}";
            }
        }

        /// <summary>
        /// Creates a reminder/task in Outlook or falls back to Microsoft To-Do web.
        /// </summary>
        public string AddReminder(
            string title,
            string dueIso = "",
            string notes = "",
            string listName = "",
            string priority = "",
            bool allDay = false)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return "Hata: Hatırlatıcı başlığı boş olamaz.";
            }

            DateTime? dueDateTime = null;
            if (!string.IsNullOrWhiteSpace(dueIso) && DateTime.TryParse(dueIso, out var parsed))
            {
                dueDateTime = parsed;
            }

            if (!_outlookAvailable || _outlookApp == null)
            {
                Logger.Information("[Reminders] Outlook not available, opening Microsoft To-Do for add");
                WinHelpers.OpenUrl("https://to-do.microsoft.com/tasks");
                var dueStr = dueDateTime.HasValue ? $" ({dueDateTime.Value:dd.MM.yyyy HH:mm})" : "";
                return $"Microsoft To-Do web açıldı. '{title}' görevi ekleyebilirsin{dueStr}.";
            }

            try
            {
                dynamic task = _outlookApp!.CreateItem(3); // olTaskItem = 3

                task.Subject = title;

                if (dueDateTime.HasValue)
                {
                    task.DueDate = dueDateTime.Value;
                    task.ReminderSet = true;
                    task.ReminderTime = dueDateTime.Value.AddHours(-1); // 1 hour before
                }

                if (!string.IsNullOrEmpty(notes))
                {
                    task.Body = notes;
                }

                // Set importance/priority: 0=Low, 1=Normal, 2=High
                task.Importance = priority?.ToLower() switch
                {
                    "high" => 2,
                    "low" => 0,
                    _ => 1
                };

                task.Save();

                Marshal.ReleaseComObject(task);

                var dueStr = dueDateTime.HasValue
                    ? $" ({dueDateTime.Value:dd.MM.yyyy HH:mm})"
                    : "";

                Logger.Information($"[Reminders] Task created: {title}{dueStr}");
                return $"Outlook'ta '{title}' görevi oluşturuldu{dueStr}.";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Reminders] Failed to create task, falling back to web");
                WinHelpers.OpenUrl("https://to-do.microsoft.com/tasks");
                return $"Microsoft To-Do web açıldı. Outlook görevi oluşturulamadı: {ex.Message}";
            }
        }

        /// <summary>
        /// Gets or creates the Outlook Application COM object.
        /// Returns null if Outlook is not installed or accessible.
        /// </summary>
        private dynamic? GetOutlookApplication()
        {
            try
            {
                var outlookType = Type.GetTypeFromProgID("Outlook.Application");
                if (outlookType == null)
                {
                    Logger.Warning("Outlook.Application ProgID not found");
                    return null;
                }

                var app = Activator.CreateInstance(outlookType);
                Logger.Information("Outlook Application COM object created successfully");
                return app;
            }
            catch (COMException ex)
            {
                Logger.Warning("COM error creating Outlook Application: {Message}", ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                Logger.Warning("Failed to create Outlook Application: {Message}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Returns a web URL fallback message for calendar operations.
        /// </summary>
        private string FallbackToWeb(string url, string query)
        {
            WinHelpers.OpenUrl(url);
            var hint = query switch
            {
                "today" => "bugün",
                "tomorrow" => "yarın",
                "next" => "sıradaki",
                "week" => "bu hafta",
                _ => query
            };
            return $"Takvim tarayıcıda açıldı ({hint}). Outlook yüklü değil, Google Calendar üzerinden görüntüleyebilirsin.";
        }

        /// <summary>
        /// Opens Google Calendar with pre-filled event data for the user to save manually.
        /// </summary>
        private string FallbackToGoogleCalendarAdd(
            string title,
            string startIso,
            string endIso,
            string location,
            string notes,
            bool allDay)
        {
            if (!DateTime.TryParse(startIso, out var startDateTime))
            {
                return $"Hata: Başlangıç tarihi anlaşılamadı: {startIso}";
            }

            var dates = BuildGoogleCalendarDateRange(startIso, endIso, allDay);
            var queryParams = new StringBuilder($"action=TEMPLATE&text={Uri.EscapeDataString(title)}");

            if (!string.IsNullOrEmpty(dates))
            {
                queryParams.Append($"&dates={dates}");
            }

            if (!string.IsNullOrEmpty(location))
            {
                queryParams.Append($"&location={Uri.EscapeDataString(location)}");
            }

            if (!string.IsNullOrEmpty(notes))
            {
                queryParams.Append($"&details={Uri.EscapeDataString(notes)}");
            }

            var url = $"https://calendar.google.com/calendar/render?{queryParams}";
            WinHelpers.OpenUrl(url);

            var whenStr = allDay
                ? startDateTime.ToString("dd.MM.yyyy")
                : startDateTime.ToString("dd.MM.yyyy HH:mm");

            Logger.Information($"[Calendar] Google Calendar fallback for add: {title}");
            return $"Google Calendar'da '{title}' etkinliği hazırlandı ({whenStr}). Tarayıcıda kaydet butonuna basarak onaylayabilirsin.";
        }

        /// <summary>
        /// Builds Google Calendar date range string in the format required by the Calendar API.
        /// </summary>
        private string BuildGoogleCalendarDateRange(string startIso, string endIso, bool allDay)
        {
            if (!DateTime.TryParse(startIso, out var startDateTime))
            {
                return "";
            }

            try
            {
                if (allDay)
                {
                    var start = startDateTime.ToString("yyyyMMdd");
                    var end = string.IsNullOrWhiteSpace(endIso) && DateTime.TryParse(endIso, out var endDt)
                        ? endDt.ToString("yyyyMMdd")
                        : startDateTime.AddDays(1).ToString("yyyyMMdd");
                    return $"{start}/{end}";
                }
                else
                {
                    var start = startDateTime.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");
                    var end = !string.IsNullOrWhiteSpace(endIso) && DateTime.TryParse(endIso, out var endDt)
                        ? endDt.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")
                        : startDateTime.AddHours(1).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'");
                    return $"{start}/{end}";
                }
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Returns date range for calendar queries.
        /// </summary>
        private (DateTime start, DateTime end) GetDateRange(string query)
        {
            var now = DateTime.Now;
            var today = now.Date;

            return query.ToLower() switch
            {
                "today" => (today, today.AddDays(1)),
                "tomorrow" => (today.AddDays(1), today.AddDays(2)),
                "week" => (today, today.AddDays(7)),
                "next" => (now, now.AddDays(30)),
                _ => (today, today.AddDays(7))
            };
        }

        /// <summary>
        /// Builds a filter string for task queries based on the query type.
        /// </summary>
        private string BuildTaskFilter(string query)
        {
            var now = DateTime.Now;
            var today = now.Date;

            return query.ToLower() switch
            {
                "today" => $"[DueDate] >= '{today:g}' AND [DueDate] <= '{today.AddDays(1):g}' AND [Complete] = false",
                "overdue" => $"[DueDate] < '{now:g}' AND [Complete] = false",
                "upcoming" => "[Complete] = false",
                _ => ""
            };
        }

        /// <summary>
        /// Releases COM objects to prevent memory leaks.
        /// </summary>
        ~OutlookComService()
        {
            if (_outlookApp != null)
            {
                try
                {
                    Marshal.ReleaseComObject(_outlookApp);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }
}
