using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    public class CorrectionEntry
    {
        public string AppName { get; set; } = string.Empty;
        public string ElementDescription { get; set; } = string.Empty;
        public string CorrectElement { get; set; } = string.Empty;
        public string? ScreenshotHash { get; set; }
        public Dictionary<string, object> VisualCharacteristics { get; set; } = new();
        public int UsageCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUsedAt { get; set; }
    }

    public class AliasEntry
    {
        public string Alias { get; set; } = string.Empty;
        public string CanonicalName { get; set; } = string.Empty;
        public string? AppName { get; set; }
    }

    public class LearningSystemService
    {
        private readonly string _correctionsFilePath;
        private readonly string _aliasesFilePath;
        private Dictionary<string, List<CorrectionEntry>> _corrections; // keyed by appName
        private List<AliasEntry> _aliases;

        // Default Turkish aliases
        private static readonly AliasEntry[] DefaultTurkishAliases = new[]
        {
            new AliasEntry { Alias = "gönder", CanonicalName = "send", AppName = null },
            new AliasEntry { Alias = "ara", CanonicalName = "search", AppName = null },
            new AliasEntry { Alias = "kaydet", CanonicalName = "save", AppName = null },
            new AliasEntry { Alias = "aç", CanonicalName = "open", AppName = null },
            new AliasEntry { Alias = "kapat", CanonicalName = "close", AppName = null },
            new AliasEntry { Alias = "sil", CanonicalName = "delete", AppName = null },
            new AliasEntry { Alias = "geri al", CanonicalName = "undo", AppName = null },
            new AliasEntry { Alias = "yeniden yap", CanonicalName = "redo", AppName = null },
            new AliasEntry { Alias = "kopyala", CanonicalName = "copy", AppName = null },
            new AliasEntry { Alias = "yapıştır", CanonicalName = "paste", AppName = null },
            new AliasEntry { Alias = "kes", CanonicalName = "cut", AppName = null },
            new AliasEntry { Alias = "yazdır", CanonicalName = "print", AppName = null },
        };

        public LearningSystemService()
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JarvisCSharp");
            Directory.CreateDirectory(appData);
            _correctionsFilePath = Path.Combine(appData, "corrections.json");
            _aliasesFilePath = Path.Combine(appData, "aliases.json");

            _corrections = new Dictionary<string, List<CorrectionEntry>>(StringComparer.OrdinalIgnoreCase);
            _aliases = new List<AliasEntry>();

            LoadCorrections();
            LoadAliases();
        }

        // --- Correction Storage ---

        public void StoreCorrection(string appName, string elementDesc, string correctElement, string? screenshotHash = null)
        {
            if (!_corrections.ContainsKey(appName))
            {
                _corrections[appName] = new List<CorrectionEntry>();
            }

            // Check for existing correction with same description
            var existing = _corrections[appName]
                .FirstOrDefault(c => c.ElementDescription.Equals(elementDesc, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.CorrectElement = correctElement;
                existing.ScreenshotHash = screenshotHash;
                existing.UsageCount++;
                existing.LastUsedAt = DateTime.UtcNow;
            }
            else
            {
                _corrections[appName].Add(new CorrectionEntry
                {
                    AppName = appName,
                    ElementDescription = elementDesc,
                    CorrectElement = correctElement,
                    ScreenshotHash = screenshotHash,
                    UsageCount = 1,
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow
                });
            }

            SaveCorrectionsAtomic();
            Logger.Information($"[LearningSystem] Stored correction for {appName}: '{elementDesc}' → '{correctElement}'");
        }

        public CorrectionEntry? GetCorrection(string appName, string elementDesc)
        {
            if (_corrections.TryGetValue(appName, out var appCorrections))
            {
                var match = appCorrections.FirstOrDefault(c =>
                    c.ElementDescription.Equals(elementDesc, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    match.UsageCount++;
                    match.LastUsedAt = DateTime.UtcNow;
                    SaveCorrectionsAtomic();
                    return match;
                }
            }
            return null;
        }

        // --- Confidence Boosting ---

        public double BoostConfidenceScore(string appName, string elementDesc, double originalScore)
        {
            if (!_corrections.TryGetValue(appName, out var appCorrections))
                return originalScore;

            // Exact match → +15
            var exactMatch = appCorrections.FirstOrDefault(c =>
                c.ElementDescription.Equals(elementDesc, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return Math.Min(100.0, originalScore + 15.0);
            }

            // Similar match (partial) → +10
            var similar = appCorrections.FirstOrDefault(c =>
                c.ElementDescription.IndexOf(elementDesc, StringComparison.OrdinalIgnoreCase) >= 0 ||
                elementDesc.IndexOf(c.ElementDescription, StringComparison.OrdinalIgnoreCase) >= 0);
            if (similar != null)
            {
                return Math.Min(100.0, originalScore + 10.0);
            }

            return originalScore;
        }

        // --- Alias System ---

        public string? ResolveAlias(string elementDesc, string? appName = null)
        {
            // Check app-specific aliases first
            if (appName != null)
            {
                var appAlias = _aliases.FirstOrDefault(a =>
                    a.AppName != null &&
                    a.AppName.Equals(appName, StringComparison.OrdinalIgnoreCase) &&
                    a.Alias.Equals(elementDesc, StringComparison.OrdinalIgnoreCase));

                if (appAlias != null) return appAlias.CanonicalName;
            }

            // Check global aliases
            var globalAlias = _aliases.FirstOrDefault(a =>
                a.AppName == null &&
                a.Alias.Equals(elementDesc, StringComparison.OrdinalIgnoreCase));

            return globalAlias?.CanonicalName;
        }

        public void AddAlias(string alias, string canonicalName, string? appName = null)
        {
            // Remove existing if same alias+app combo
            _aliases.RemoveAll(a =>
                a.Alias.Equals(alias, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.AppName, appName, StringComparison.OrdinalIgnoreCase));

            _aliases.Add(new AliasEntry
            {
                Alias = alias,
                CanonicalName = canonicalName,
                AppName = appName
            });

            SaveAliases();
            Logger.Information($"[LearningSystem] Added alias: '{alias}' → '{canonicalName}' (app: {appName ?? "global"})");
        }

        // --- Custom Script Suggestion ---

        public bool ShouldSuggestCustomScript(string appName)
        {
            if (!_corrections.TryGetValue(appName, out var appCorrections))
                return false;

            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            var recentCorrections = appCorrections.Count(c => c.LastUsedAt >= sevenDaysAgo);

            return recentCorrections > 5;
        }

        public List<CorrectionEntry> GetFrequentCorrections(string appName, int minUsage = 3)
        {
            if (!_corrections.TryGetValue(appName, out var appCorrections))
                return new List<CorrectionEntry>();

            return appCorrections
                .Where(c => c.UsageCount >= minUsage)
                .OrderByDescending(c => c.UsageCount)
                .ToList();
        }

        // --- Persistence ---

        private void SaveCorrectionsAtomic()
        {
            try
            {
                var json = JsonSerializer.Serialize(_corrections, new JsonSerializerOptions { WriteIndented = true });
                var tempPath = _correctionsFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _correctionsFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[LearningSystem] Failed to save corrections");
            }
        }

        private void LoadCorrections()
        {
            try
            {
                if (File.Exists(_correctionsFilePath))
                {
                    var json = File.ReadAllText(_correctionsFilePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, List<CorrectionEntry>>>(json);
                    if (data != null) _corrections = new Dictionary<string, List<CorrectionEntry>>(data, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[LearningSystem] Failed to load corrections");
            }
        }

        private void SaveAliases()
        {
            try
            {
                var json = JsonSerializer.Serialize(_aliases, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_aliasesFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[LearningSystem] Failed to save aliases");
            }
        }

        private void LoadAliases()
        {
            try
            {
                if (File.Exists(_aliasesFilePath))
                {
                    var json = File.ReadAllText(_aliasesFilePath);
                    var data = JsonSerializer.Deserialize<List<AliasEntry>>(json);
                    if (data != null) _aliases = data;
                }
                else
                {
                    // Initialize with default Turkish aliases
                    _aliases.AddRange(DefaultTurkishAliases);
                    SaveAliases();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[LearningSystem] Failed to load aliases");
            }
        }
    }
}
