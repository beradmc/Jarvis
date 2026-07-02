using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    public class ActionLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string TargetApplication { get; set; } = string.Empty;
        public string TargetWindow { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ScreenshotPath { get; set; }
    }

    public class ActionLog : IDisposable
    {
        private readonly string _logDirectory;
        private readonly string _currentLogFile;
        private readonly List<ActionLogEntry> _entries;
        private readonly object _lock = new();
        private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
        private const int MaxRotatedFiles = 5;

        public ActionLog()
        {
            _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JarvisCSharp", "action_logs");
            Directory.CreateDirectory(_logDirectory);
            _currentLogFile = Path.Combine(_logDirectory, "action_log.jsonl");
            _entries = new List<ActionLogEntry>();

            LoadCurrentLog();
        }

        public void Record(ActionLogEntry entry)
        {
            lock (_lock)
            {
                _entries.Add(entry);
                AppendToFile(entry);
                CheckRotation();
            }
        }

        public ActionLogEntry RecordAction(string actionType, string targetApp, string targetWindow,
            Dictionary<string, object> parameters, bool success, string? errorMessage = null,
            TimeSpan? duration = null, string? screenshotPath = null)
        {
            var entry = new ActionLogEntry
            {
                Timestamp = DateTime.UtcNow,
                ActionType = actionType,
                TargetApplication = targetApp,
                TargetWindow = targetWindow,
                Parameters = parameters,
                Success = success,
                ErrorMessage = errorMessage,
                Duration = duration ?? TimeSpan.Zero,
                ScreenshotPath = screenshotPath
            };
            Record(entry);
            return entry;
        }

        public List<ActionLogEntry> GetAll()
        {
            lock (_lock) { return _entries.ToList(); }
        }

        public List<ActionLogEntry> Filter(DateTime? startTime = null, DateTime? endTime = null,
            string? application = null, string? actionType = null, bool? success = null)
        {
            lock (_lock)
            {
                IEnumerable<ActionLogEntry> query = _entries;

                if (startTime.HasValue)
                    query = query.Where(e => e.Timestamp >= startTime.Value);
                if (endTime.HasValue)
                    query = query.Where(e => e.Timestamp <= endTime.Value);
                if (!string.IsNullOrEmpty(application))
                    query = query.Where(e => e.TargetApplication.IndexOf(application, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrEmpty(actionType))
                    query = query.Where(e => e.ActionType.Equals(actionType, StringComparison.OrdinalIgnoreCase));
                if (success.HasValue)
                    query = query.Where(e => e.Success == success.Value);

                return query.ToList();
            }
        }

        public List<ActionLogEntry> GetRecent(int count = 50)
        {
            lock (_lock)
            {
                return _entries.AsEnumerable().Reverse().Take(count).Reverse().ToList();
            }
        }

        public List<ActionLogEntry> GetLogRange(DateTime startTime, DateTime endTime)
        {
            return Filter(startTime: startTime, endTime: endTime);
        }

        public string ExportLog(string format = "json")
        {
            var entries = GetAll();
            var options = new JsonSerializerOptions { WriteIndented = true };

            switch (format.ToLower())
            {
                case "json":
                    return JsonSerializer.Serialize(entries, options);

                case "csv":
                    var lines = new List<string>
                    {
                        "Timestamp,ActionType,TargetApplication,TargetWindow,Success,ErrorMessage,DurationMs"
                    };
                    foreach (var e in entries)
                    {
                        lines.Add($"\"{e.Timestamp:O}\",\"{Escape(e.ActionType)}\",\"{Escape(e.TargetApplication)}\",\"{Escape(e.TargetWindow)}\",{e.Success},\"{Escape(e.ErrorMessage ?? "")}\",{e.Duration.TotalMilliseconds}");
                    }
                    return string.Join(Environment.NewLine, lines);

                default:
                    throw new ArgumentException($"Unsupported format: {format}");
            }
        }

        public string GetHumanReadableSummary(int count = 50)
        {
            var recent = GetRecent(count);
            var lines = new List<string> { $"=== Son {recent.Count} İşlem ===" };

            foreach (var e in recent)
            {
                var status = e.Success ? "✅" : "❌";
                var timeStr = e.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                lines.Add($"[{timeStr}] {status} {e.ActionType} → {e.TargetApplication}/{e.TargetWindow} ({e.Duration.TotalMilliseconds:F0}ms)");
                if (!e.Success && !string.IsNullOrEmpty(e.ErrorMessage))
                {
                    lines.Add($"         Hata: {e.ErrorMessage}");
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void AppendToFile(ActionLogEntry entry)
        {
            try
            {
                var json = JsonSerializer.Serialize(entry);
                File.AppendAllText(_currentLogFile, json + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ActionLog] Failed to append to file");
            }
        }

        private void CheckRotation()
        {
            try
            {
                if (!File.Exists(_currentLogFile)) return;
                var fileInfo = new FileInfo(_currentLogFile);
                if (fileInfo.Length <= MaxFileSizeBytes) return;

                // Rotate
                for (int i = MaxRotatedFiles - 1; i >= 1; i--)
                {
                    var src = Path.Combine(_logDirectory, $"action_log.{i}.jsonl");
                    var dst = Path.Combine(_logDirectory, $"action_log.{i + 1}.jsonl");
                    if (File.Exists(src))
                    {
                        if (i + 1 >= MaxRotatedFiles)
                            File.Delete(src);
                        else
                            File.Move(src, dst, overwrite: true);
                    }
                }

                var firstRotated = Path.Combine(_logDirectory, "action_log.1.jsonl");
                File.Move(_currentLogFile, firstRotated, overwrite: true);

                Logger.Information("[ActionLog] Log rotated");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ActionLog] Rotation failed");
            }
        }

        private void LoadCurrentLog()
        {
            try
            {
                if (!File.Exists(_currentLogFile)) return;
                var lines = File.ReadAllLines(_currentLogFile);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<ActionLogEntry>(line);
                        if (entry != null) _entries.Add(entry);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ActionLog] Failed to load log");
            }
        }

        private static string Escape(string s)
        {
            return s.Replace("\"", "\"\"");
        }

        public void Dispose()
        {
            // Flush if needed
        }
    }
}
