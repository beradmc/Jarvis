using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services
{
    /// <summary>
    /// Service for reading and parsing iPhone Health Auto Export JSON files from iCloud Drive.
    /// Implements Requirements 11, 26 - Health Data Import and Parsing
    /// </summary>
    public class HealthDataService
    {
        private const int StaleWarnMinutes = 120; // 2 hours

        // iCloud Drive base path
        private static readonly string ICloudBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "iCloudDrive");

        // Search directories in priority order
        private static readonly string[] SearchDirs = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "iCloudDrive", "iCloud~com~ifunography~HealthExport", "Documents", "JARVIS"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "iCloudDrive", "Auto Export", "JARVIS"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "iCloudDrive", "JARVIS"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "OneDrive", "JARVIS"),
        };

        // Legacy flat file path for backward compatibility
        private static readonly string LegacyFile = Path.Combine(ICloudBase, "JARVIS", "health_data.json");

        /// <summary>
        /// Get health data based on query string.
        /// Supports date extraction (today, yesterday, specific dates) and metric filtering.
        /// </summary>
        public string GetHealthData(string query = "all")
        {
            DateTime? targetDate = ExtractTargetDate(query);
            string? filePath = ResolvePath(targetDate);

            if (filePath == null)
            {
                return "Sağlık verisi bulunamadı. " +
                       "iPhone'da desteklenen bir sağlık dışa aktarma aracı kurup " +
                       "iCloud Drive > Auto Export > JARVIS klasörüne export ayarlaman gerekiyor.";
            }

            try
            {
                var (data, timestamp, rawJson, sourceDate) = LoadHealthFile(filePath, targetDate);

                if (data == null || !data.Any())
                    return "Sağlık dosyası boş veya tanınmayan formatta.";

                string age = GetFileAge(timestamp);
                string normalized = NormalizeQuery(query);

                string result;
                if (IsWorkoutAnalysisQuery(normalized))
                {
                    result = BuildHealthAnalysis(rawJson, data, query, sourceDate, age);
                }
                else
                {
                    result = FormatHealthData(data, query, age);
                }

                // Add staleness warning
                double minsOld = (DateTime.Now - timestamp).TotalMinutes;
                if (minsOld > StaleWarnMinutes)
                {
                    result += $"\n⚠️  Veri {age} güncellendi — uygulamanın otomatik export ayarını kontrol et.";
                }

                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load health data");
                return $"Sağlık dosyası okunamadı: {ex.Message}";
            }
        }

        /// <summary>
        /// Find the most recent HealthAutoExport-*.json file, optionally for a specific target date.
        /// Searches iCloud Drive paths first, then falls back to legacy locations.
        /// </summary>
        public string? FindLatestHealthFile(DateTime? targetDate = null)
        {
            if (targetDate.HasValue)
            {
                string targetName = $"HealthAutoExport-{targetDate.Value:yyyy-MM-dd}.json";
                foreach (var dir in SearchDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    string candidate = Path.Combine(dir, targetName);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            // Search for latest file in each directory
            foreach (var dir in SearchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                var files = Directory.GetFiles(dir, "HealthAutoExport-*.json")
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToArray();

                if (files.Length > 0)
                    return files[0];
            }

            return null;
        }

        /// <summary>
        /// Resolve health file path with fallbacks for legacy support.
        /// Returns path to most recent health export file or null if not found.
        /// </summary>
        public string? ResolvePath(DateTime? targetDate = null)
        {
            // Try new HealthAutoExport format first
            string? latestExport = FindLatestHealthFile(targetDate);
            if (latestExport != null)
                return latestExport;

            // Fall back to legacy flat file
            if (File.Exists(LegacyFile))
                return LegacyFile;

            return null;
        }

        /// <summary>
        /// Load and parse health file, returning (data dict, timestamp, raw JSON, source date).
        /// </summary>
        private (Dictionary<string, double?>, DateTime, JsonDocument?, DateTime?) LoadHealthFile(
            string filePath, DateTime? targetDate)
        {
            var fileInfo = new FileInfo(filePath);
            var fileMtime = fileInfo.LastWriteTime;
            var sourceDate = DateFromFile(filePath) ?? targetDate;

            string jsonContent = File.ReadAllText(filePath);
            var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            // Check for simple format (legacy)
            if (root.TryGetProperty("data", out var dataElement) && 
                dataElement.ValueKind == JsonValueKind.Object &&
                !dataElement.TryGetProperty("metrics", out _))
            {
                var simpleData = new Dictionary<string, double?>();
                foreach (var prop in dataElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        simpleData[prop.Name] = prop.Value.GetDouble();
                }
                
                DateTime timestamp = fileMtime;
                if (root.TryGetProperty("timestamp", out var tsElement) && tsElement.ValueKind == JsonValueKind.Number)
                    timestamp = DateTimeOffset.FromUnixTimeSeconds((long)tsElement.GetDouble()).DateTime;

                return (simpleData, timestamp, jsonDoc, sourceDate);
            }

            // Parse HealthAutoExport format
            var parsedData = ParseHealthAutoExport(root, sourceDate);
            return (parsedData, fileMtime, jsonDoc, sourceDate);
        }

        /// <summary>
        /// Parse HealthAutoExport JSON format and extract metrics.
        /// </summary>
        private Dictionary<string, double?> ParseHealthAutoExport(JsonElement root, DateTime? targetDate)
        {
            var result = new Dictionary<string, double?>();

            // Get metrics array
            JsonElement metricsArray;
            if (root.TryGetProperty("data", out var dataEl) && dataEl.TryGetProperty("metrics", out metricsArray))
            {
                // Combined format: {"data": {"metrics": [...]}}
            }
            else if (root.TryGetProperty("metrics", out metricsArray))
            {
                // Alternative format: {"metrics": [...]}
            }
            else
            {
                return result; // No metrics found
            }

            if (metricsArray.ValueKind != JsonValueKind.Array)
                return result;

            string dayKey = targetDate?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd");

            // Map from export field names to our internal keys
            var nameMap = new Dictionary<string, (string key, string mode)>
            {
                ["heart_rate"] = ("heart_rate", "latest"),
                ["resting_heart_rate"] = ("resting_hr", "latest"),
                ["heart_rate_variability"] = ("hrv", "latest"),
                ["heart_rate_variability_sdnn"] = ("hrv", "latest"),
                ["heartratevariabilitysdnn"] = ("hrv", "latest"),
                ["blood_oxygen_saturation"] = ("blood_oxygen_raw", "latest"),
                ["oxygen_saturation"] = ("blood_oxygen_raw", "latest"),
                ["respiratory_rate"] = ("respiratory_rate", "latest"),
                ["step_count"] = ("steps", "today_sum"),
                ["steps"] = ("steps", "today_sum"),
                ["active_energy"] = ("calories", "today_sum"),
                ["active_energy_burned"] = ("calories", "today_sum"),
                ["basal_energy_burned"] = ("basal_calories", "today_sum"),
                ["apple_exercise_time"] = ("exercise_min", "today_sum"),
                ["exercise_time"] = ("exercise_min", "today_sum"),
                ["exercise_minutes"] = ("exercise_min", "today_sum"),
                ["apple_stand_hour"] = ("stand_hours", "today_sum"),
                ["apple_stand_time"] = ("stand_min", "today_sum"),
                ["time_in_daylight"] = ("daylight_min", "today_sum"),
                ["flights_climbed"] = ("flights_climbed", "today_sum"),
                ["walking_heart_rate_average"] = ("walking_hr", "latest"),
                ["walking_speed"] = ("walking_speed", "latest"),
                ["walking_asymmetry_percentage"] = ("walking_asymmetry_pct", "latest"),
                ["walking_double_support_percentage"] = ("walking_double_support_pct", "latest"),
                ["walking_step_length"] = ("walking_step_length_cm", "latest"),
                ["walking_running_distance"] = ("walking_distance_km", "today_sum"),
                ["environmental_audio_exposure"] = ("environment_audio_db", "latest"),
                ["headphone_audio_exposure"] = ("headphone_audio_db", "latest"),
                ["physical_effort"] = ("physical_effort", "latest"),
                ["sleep_analysis"] = ("sleep_hours", "latest"),
                ["sleep_duration"] = ("sleep_hours", "latest"),
            };

            foreach (var metricEl in metricsArray.EnumerateArray())
            {
                if (!metricEl.TryGetProperty("name", out var nameEl))
                    continue;

                string rawName = nameEl.GetString()?.ToLower().Replace(" ", "_") ?? "";
                if (!nameMap.TryGetValue(rawName, out var mapping))
                    continue;

                if (!metricEl.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                    continue;

                var entries = dataArray.EnumerateArray().ToList();
                if (entries.Count == 0)
                    continue;

                var (ourKey, mode) = mapping;
                double? value = null;

                if (mode == "latest")
                {
                    value = LatestQty(entries);
                }
                else if (mode == "today_sum")
                {
                    value = TodaySum(entries, dayKey);
                }

                if (value.HasValue)
                    result[ourKey] = value.Value;
            }

            // Normalize blood oxygen from 0.0-1.0 to 0-100 percentage
            if (result.TryGetValue("blood_oxygen_raw", out var rawO2) && rawO2.HasValue)
            {
                result.Remove("blood_oxygen_raw");
                result["blood_oxygen"] = rawO2.Value <= 1.0 ? rawO2.Value * 100 : rawO2.Value;
            }

            return result;
        }

        /// <summary>
        /// Extract the latest quantity value from metric entries.
        /// </summary>
        private double? LatestQty(List<JsonElement> entries)
        {
            // Iterate from end (most recent)
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                var keys = new[] { "qty", "value", "Avg", "avg" };
                
                foreach (var key in keys)
                {
                    if (entry.TryGetProperty(key, out var valEl) && valEl.ValueKind == JsonValueKind.Number)
                    {
                        return valEl.GetDouble();
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Sum all quantity values for the target day.
        /// </summary>
        private double TodaySum(List<JsonElement> entries, string dayKey)
        {
            double total = 0.0;
            foreach (var entry in entries)
            {
                if (!entry.TryGetProperty("date", out var dateEl))
                    continue;

                string dateStr = dateEl.GetString() ?? "";
                if (!dateStr.Contains(dayKey))
                    continue;

                var keys = new[] { "qty", "value" };
                foreach (var key in keys)
                {
                    if (entry.TryGetProperty(key, out var valEl) && valEl.ValueKind == JsonValueKind.Number)
                    {
                        total += valEl.GetDouble();
                        break;
                    }
                }
            }
            return total;
        }

        /// <summary>
        /// Extract target date from query string (today, yesterday, specific dates).
        /// </summary>
        private DateTime? ExtractTargetDate(string query)
        {
            string q = NormalizeQuery(query);
            DateTime today = DateTime.Today;

            // Check for "two days ago" type queries
            if (q.Contains("onceki gun") || q.Contains("evvelsi gun") || q.Contains("iki gun once"))
                return today.AddDays(-2);

            // Check for yesterday
            if (q.Contains("dun") || q.Contains("yesterday"))
                return today.AddDays(-1);

            // Check for today
            if (q.Contains("bugun") || q.Contains("today") || q.Contains("simdi"))
                return today;

            // Try ISO date format (YYYY-MM-DD)
            var isoMatch = Regex.Match(q, @"\b(20\d{2}-\d{2}-\d{2})\b");
            if (isoMatch.Success)
            {
                if (DateTime.TryParseExact(isoMatch.Groups[1].Value, "yyyy-MM-dd", 
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
                    return isoDate;
            }

            // Try Turkish date format (DD.MM.YYYY or DD/MM/YYYY)
            var trMatch = Regex.Match(q, @"\b(\d{1,2})[./-](\d{1,2})[./-](20\d{2})\b");
            if (trMatch.Success)
            {
                int day = int.Parse(trMatch.Groups[1].Value);
                int month = int.Parse(trMatch.Groups[2].Value);
                int year = int.Parse(trMatch.Groups[3].Value);
                
                try
                {
                    return new DateTime(year, month, day);
                }
                catch
                {
                    // Invalid date, ignore
                }
            }

            return null;
        }

        /// <summary>
        /// Normalize query string (lowercase, remove Turkish diacritics).
        /// </summary>
        private string NormalizeQuery(string text)
        {
            text = (text ?? "").Trim().ToLower();
            text = text.Replace("ı", "i")
                      .Replace("ğ", "g")
                      .Replace("ü", "u")
                      .Replace("ş", "s")
                      .Replace("ö", "o")
                      .Replace("ç", "c");
            return text;
        }

        /// <summary>
        /// Get human-readable age string from file timestamp.
        /// </summary>
        private string GetFileAge(DateTime timestamp)
        {
            double mins = (DateTime.Now - timestamp).TotalMinutes;
            
            if (mins < 2) return "az önce";
            if (mins < 60) return $"{(int)mins} dakika önce";
            
            double hrs = mins / 60;
            if (hrs < 24) return $"{hrs:F1} saat önce";
            
            return $"{hrs / 24:F1} gün önce";
        }

        /// <summary>
        /// Check if query is asking for workout analysis.
        /// </summary>
        private bool IsWorkoutAnalysisQuery(string normalized)
        {
            var keywords = new[] { "antren", "antreman", "workout", "egzersiz", "spor", "fitness", "analiz", "yorum", "detay", "detayli" };
            return keywords.Any(k => normalized.Contains(k));
        }

        /// <summary>
        /// Format health data based on query filter.
        /// </summary>
        private string FormatHealthData(Dictionary<string, double?> data, string query, string age)
        {
            string q = query.ToLower();

            // Heart rate queries
            if (q.Contains("nabız") || q.Contains("nabiz") || q.Contains("kalp") || 
                q.Contains("heart") || q.Contains("bpm") || q.Contains("hrv"))
            {
                return string.Join("\n", new[]
                {
                    $"Anlık nabız    : {V(data, "heart_rate", " bpm")}",
                    $"Dinlenim nabzı : {V(data, "resting_hr", " bpm")}",
                    $"HRV            : {V(data, "hrv", " ms", 1)}",
                    $"Yürüyüş nabzı  : {V(data, "walking_hr", " bpm")}",
                    $"[güncelleme: {age}]"
                });
            }

            // Activity queries
            if (q.Contains("adım") || q.Contains("step") || q.Contains("egzersiz") || 
                q.Contains("exercise") || q.Contains("kalori") || q.Contains("aktivite") ||
                q.Contains("activity") || q.Contains("kardiyo") || q.Contains("stand") ||
                q.Contains("ayakta") || q.Contains("mesafe") || q.Contains("distance") || q.Contains("kat"))
            {
                return string.Join("\n", new[]
                {
                    $"Bugün adım     : {V(data, "steps")}",
                    $"Aktif kalori   : {V(data, "calories", " kcal")}",
                    $"Bazal kalori   : {V(data, "basal_calories", " kcal")}",
                    $"Egzersiz süresi: {V(data, "exercise_min", " dk")}",
                    $"Ayakta saat    : {V(data, "stand_hours", " saat")}",
                    $"Ayakta süre    : {V(data, "stand_min", " dk")}",
                    $"Yürüme mesafesi: {V(data, "walking_distance_km", " km", 2)}",
                    $"Çıkılan kat    : {V(data, "flights_climbed")}",
                    $"[güncelleme: {age}]"
                });
            }

            // Walking queries
            if (q.Contains("yürüyüş") || q.Contains("yuruyus") || q.Contains("yürüme") || 
                q.Contains("yurume") || q.Contains("walking") || q.Contains("mobility") ||
                q.Contains("denge") || q.Contains("asimetri") || q.Contains("asymmetry") ||
                q.Contains("hız") || q.Contains("hiz") || q.Contains("adım uzunluğu") || 
                q.Contains("adim uzunlugu") || q.Contains("step length") || q.Contains("double support"))
            {
                return string.Join("\n", new[]
                {
                    $"Yürüme hızı    : {V(data, "walking_speed", " km/sa", 1)}",
                    $"Adım uzunluğu  : {V(data, "walking_step_length_cm", " cm")}",
                    $"Yürüyüş nabzı  : {V(data, "walking_hr", " bpm")}",
                    $"Asimetri       : {V(data, "walking_asymmetry_pct", "%", 1)}",
                    $"Double support : {V(data, "walking_double_support_pct", "%", 1)}",
                    $"Mesafe         : {V(data, "walking_distance_km", " km", 2)}",
                    $"[güncelleme: {age}]"
                });
            }

            // Sleep queries
            if (q.Contains("uyku") || q.Contains("sleep") || q.Contains("uyudum"))
            {
                return string.Join("\n", new[]
                {
                    $"Uyku süresi    : {V(data, "sleep_hours", " saat", 1)}",
                    $"Derin uyku     : {V(data, "deep_sleep_hours", " saat", 1)}",
                    $"REM uyku       : {V(data, "rem_sleep_hours", " saat", 1)}",
                    $"[güncelleme: {age}]"
                });
            }

            // Oxygen queries
            if (q.Contains("oksijen") || q.Contains("spo2") || q.Contains("oxygen") || q.Contains("solunum"))
            {
                return string.Join("\n", new[]
                {
                    $"Kan oksijeni   : {V(data, "blood_oxygen", "%", 1)}",
                    $"Solunum hızı   : {V(data, "respiratory_rate", " nefes/dk", 1)}",
                    $"[güncelleme: {age}]"
                });
            }

            // Audio exposure queries
            if (q.Contains("ses") || q.Contains("audio") || q.Contains("kulaklık") || 
                q.Contains("kulaklik") || q.Contains("maruziyet") || q.Contains("gürültü") ||
                q.Contains("gurultu") || q.Contains("desibel") || q.Contains("db") ||
                q.Contains("headphone") || q.Contains("environmental"))
            {
                return string.Join("\n", new[]
                {
                    $"Çevresel ses   : {V(data, "environment_audio_db", " dB", 1)}",
                    $"Kulaklık sesi  : {V(data, "headphone_audio_db", " dB", 1)}",
                    $"[güncelleme: {age}]"
                });
            }

            // Daylight queries
            if (q.Contains("gün ışığı") || q.Contains("gun isigi") || q.Contains("daylight") ||
                q.Contains("güneş") || q.Contains("gunes"))
            {
                return string.Join("\n", new[]
                {
                    $"Gün ışığı      : {V(data, "daylight_min", " dk", 1)}",
                    $"Physical effort: {V(data, "physical_effort", "", 1)}",
                    $"[güncelleme: {age}]"
                });
            }

            // Default: full summary
            return string.Join("\n", new[]
            {
                "── SAĞLIK ÖZETİ ──────────────────",
                $"💓 Nabız         : {V(data, "heart_rate", " bpm")}  (din.: {V(data, "resting_hr", " bpm")})",
                $"📊 HRV           : {V(data, "hrv", " ms", 1)}",
                $"🚶 Yürüyüş nabzı : {V(data, "walking_hr", " bpm")}",
                $"🩸 Kan oksijeni  : {V(data, "blood_oxygen", "%", 1)}",
                $"🫁 Solunum hızı  : {V(data, "respiratory_rate", " nefes/dk", 1)}",
                $"👣 Adım          : {V(data, "steps")}",
                $"🔥 Aktif kalori  : {V(data, "calories", " kcal")}",
                $"⚡ Bazal kalori  : {V(data, "basal_calories", " kcal")}",
                $"🏃 Egzersiz      : {V(data, "exercise_min", " dk")}",
                $"🧍 Stand         : {V(data, "stand_hours", " saat")}  ({V(data, "stand_min", " dk")})",
                $"🪜 Çıkılan kat   : {V(data, "flights_climbed")}",
                $"📏 Mesafe        : {V(data, "walking_distance_km", " km", 2)}",
                $"🚀 Yürüme hızı   : {V(data, "walking_speed", " km/sa", 1)}",
                $"📐 Adım uzunluğu : {V(data, "walking_step_length_cm", " cm")}",
                $"🎧 Kulaklık sesi : {V(data, "headphone_audio_db", " dB", 1)}",
                $"🌤 Gün ışığı     : {V(data, "daylight_min", " dk", 1)}",
                $"💤 Uyku          : {V(data, "sleep_hours", " saat", 1)}",
                $"──────────────────────────────────",
                $"[güncelleme: {age}]"
            });
        }

        /// <summary>
        /// Build detailed workout analysis.
        /// </summary>
        private string BuildHealthAnalysis(JsonDocument? rawJson, Dictionary<string, double?> data, 
            string query, DateTime? sourceDate, string age)
        {
            string period = PeriodLabel(sourceDate);
            double exerciseMin = GetDouble(data, "exercise_min") ?? 0.0;
            double calories = GetDouble(data, "calories") ?? 0.0;
            double steps = GetDouble(data, "steps") ?? 0.0;
            double distance = GetDouble(data, "walking_distance_km") ?? 0.0;
            double? walkingSpeed = GetDouble(data, "walking_speed");
            double? hrv = GetDouble(data, "hrv");
            double? restingHr = GetDouble(data, "resting_hr");
            double? currentHr = GetDouble(data, "heart_rate");
            double? effort = GetDouble(data, "physical_effort");

            var lines = new List<string>();

            // Determine if this was a workout day
            string normalized = NormalizeQuery(query);
            if (normalized.Contains("antren") || normalized.Contains("antreman") || 
                normalized.Contains("workout") || normalized.Contains("egzersiz") || 
                normalized.Contains("spor") || normalized.Contains("fitness"))
            {
                if (exerciseMin >= 10)
                    lines.Add($"{period} için evet, belirgin bir antrenman/egzersiz kaydı var.");
                else if (exerciseMin > 0)
                    lines.Add($"{period} için kısa süreli hareket kaydı var ama tam bir antrenman kadar güçlü görünmüyor.");
                else
                    lines.Add($"{period} için belirgin bir antrenman kaydı görünmüyor.");
            }
            else
            {
                lines.Add($"{period} sağlık analizi hazır.");
            }

            lines.Add($"Egzersiz süresi: {(int)Math.Round(exerciseMin)} dakika.");
            lines.Add($"Aktif kalori: {(int)Math.Round(calories)} kcal, adım: {(int)Math.Round(steps)}, mesafe: {distance:F2} km.");

            // Workout intensity assessment
            if (exerciseMin >= 60 || calories >= 600)
                lines.Add("Yorum: yük oldukça yüksek, gün aktif geçmiş.");
            else if (exerciseMin >= 30 || calories >= 300)
                lines.Add("Yorum: orta seviye, verimli bir aktivite günü.");
            else if (steps >= 7000)
                lines.Add("Yorum: belirgin bir yürüyüş/aktif yaşam günü.");
            else
                lines.Add("Yorum: yük hafif, daha çok günlük hareket düzeyinde.");

            // Recovery assessment
            var recoveryNotes = new List<string>();
            if (hrv.HasValue)
            {
                if (hrv.Value >= 70)
                    recoveryNotes.Add($"HRV {hrv.Value:F1} ms ile iyi görünüyor");
                else if (hrv.Value >= 40)
                    recoveryNotes.Add($"HRV {hrv.Value:F1} ms ile orta seviyede");
                else
                    recoveryNotes.Add($"HRV {hrv.Value:F1} ms ile düşük tarafta");
            }

            if (restingHr.HasValue)
                recoveryNotes.Add($"dinlenim nabzı {(int)Math.Round(restingHr.Value)} bpm");
            else if (currentHr.HasValue)
                recoveryNotes.Add($"son nabız ölçümü {(int)Math.Round(currentHr.Value)} bpm");

            if (recoveryNotes.Any())
                lines.Add("Toparlanma: " + string.Join(", ", recoveryNotes) + ".");

            if (walkingSpeed.HasValue)
                lines.Add($"Yürüme hızı yaklaşık {walkingSpeed.Value:F1} km/sa.");

            if (effort.HasValue)
                lines.Add($"Physical effort skoru yaklaşık {effort.Value:F1}.");

            lines.Add($"[güncelleme: {age}]");

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Format value with unit for display.
        /// </summary>
        private string V(Dictionary<string, double?> data, string key, string unit = "", int decimals = 0)
        {
            if (!data.TryGetValue(key, out var value) || !value.HasValue)
                return "—";

            double val = value.Value;
            if (decimals == 0)
                return $"{(int)Math.Round(val)}{unit}";
            else
                return $"{val.ToString($"F{decimals}")}{unit}";
        }

        /// <summary>
        /// Get double value from data dictionary.
        /// </summary>
        private double? GetDouble(Dictionary<string, double?> data, string key)
        {
            return data.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// Extract date from filename (HealthAutoExport-YYYY-MM-DD.json).
        /// </summary>
        private DateTime? DateFromFile(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            var match = Regex.Match(Path.GetFileName(filePath), @"HealthAutoExport-(\d{4}-\d{2}-\d{2})");
            if (!match.Success)
                return null;

            if (DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", 
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;

            return null;
        }

        /// <summary>
        /// Get period label for display.
        /// </summary>
        private string PeriodLabel(DateTime? targetDate)
        {
            if (!targetDate.HasValue)
                return "Seçili gün";

            if (targetDate.Value.Date == DateTime.Today)
                return "Bugün";

            if (targetDate.Value.Date == DateTime.Today.AddDays(-1))
                return "Dün";

            return targetDate.Value.ToString("dd MMMM yyyy", new CultureInfo("tr-TR"));
        }
    }
}
