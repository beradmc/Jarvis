using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    public class ResearchAgentService : IResearchAgentService
    {
        private readonly Dictionary<string, (ResearchResult Result, DateTime CachedAt)> _cache;
        private readonly Dictionary<string, List<AutomationPattern>> _knowledgeBase;
        private readonly string _cacheDir;
        private readonly string _knowledgeBasePath;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromDays(7);

        public ResearchAgentService()
        {
            _cache = new Dictionary<string, (ResearchResult, DateTime)>(StringComparer.OrdinalIgnoreCase);
            _knowledgeBase = new Dictionary<string, List<AutomationPattern>>(StringComparer.OrdinalIgnoreCase);

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JarvisCSharp");
            _cacheDir = Path.Combine(appData, "research_cache");
            _knowledgeBasePath = Path.Combine(appData, "knowledge_base.json");
            Directory.CreateDirectory(_cacheDir);

            LoadKnowledgeBase();
            LoadCachedResults();
        }

        public ApplicationFramework DetectFramework(IntPtr windowHandle)
        {
            try
            {
                // Get window class name
                var classNameBuilder = new StringBuilder(256);
                GetClassName(windowHandle, classNameBuilder, 256);
                var className = classNameBuilder.ToString();

                // Get process info
                GetWindowThreadProcessId(windowHandle, out uint processId);
                var process = Process.GetProcessById((int)processId);
                var processName = process.ProcessName.ToLower();
                string? mainModulePath = null;
                try { mainModulePath = process.MainModule?.FileName?.ToLower(); } catch { }

                // WPF detection
                if (className.StartsWith("HwndWrapper") || className.Contains("WPF"))
                    return ApplicationFramework.WPF;

                // WinForms detection
                if (className.StartsWith("WindowsForms"))
                    return ApplicationFramework.WinForms;

                // Electron detection
                if (className == "Chrome_WidgetWin_1" || className == "Electron")
                {
                    // Check if it's Chrome vs Electron
                    if (processName != "chrome" && processName != "msedge" && processName != "brave")
                        return ApplicationFramework.Electron;
                }

                // UWP detection
                if (className.StartsWith("Windows.UI.Core") || className == "ApplicationFrameWindow")
                    return ApplicationFramework.UWP;

                // Qt detection
                if (className.StartsWith("Qt") || className.Contains("QWidget"))
                    return ApplicationFramework.Qt;

                // Browser/Web detection
                if (processName == "chrome" || processName == "msedge" || processName == "firefox" || processName == "brave")
                    return ApplicationFramework.Web;

                return ApplicationFramework.Win32;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ResearchAgent] Framework detection failed: {ex.Message}");
                return ApplicationFramework.Unknown;
            }
        }

        public async Task<ResearchResult> ResearchApplicationAsync(string applicationName)
        {
            // Check cache first
            var cached = GetCachedResearch(applicationName);
            if (cached != null) return cached;

            var result = new ResearchResult
            {
                ApplicationName = applicationName,
                ResearchTime = DateTime.UtcNow,
                IsCached = false
            };

            // Search GitHub
            try
            {
                result.GitHubResults = await SearchGitHubAsync($"{applicationName} automation UI");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ResearchAgent] GitHub search failed: {ex.Message}");
            }

            // Search documentation
            try
            {
                result.DocumentationLinks = await SearchDocumentationAsync(applicationName);
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ResearchAgent] Documentation search failed: {ex.Message}");
            }

            // Get known patterns from knowledge base
            if (_knowledgeBase.TryGetValue(applicationName, out var patterns))
            {
                result.Patterns = patterns;
            }

            // Suggest patterns
            result.Patterns.AddRange(GenerateDefaultPatterns(result.Framework));

            // Cache the result
            CacheResult(applicationName, result);

            return result;
        }

        public async Task<List<GitHubResult>> SearchGitHubAsync(string query)
        {
            // In a real implementation, this would use the GitHub API or web search
            // For now, return well-known automation repositories
            var results = new List<GitHubResult>();

            var knownRepos = new Dictionary<string, (string Title, string Desc, int Stars)>
            {
                { "FlaUI", ("FlaUI/FlaUI", "UI Automation library for .NET", 2500) },
                { "Appium", ("appium/appium", "Automation for Apps", 18000) },
                { "WinAppDriver", ("microsoft/WinAppDriver", "Windows Application Driver", 3500) },
                { "AutoIt", ("AutoIt", "AutoIt v3 scripting language", 1000) },
                { "pywinauto", ("pywinauto/pywinauto", "Python automation for Windows", 4600) },
            };

            var queryLower = query.ToLower();
            foreach (var repo in knownRepos)
            {
                results.Add(new GitHubResult
                {
                    Repository = repo.Value.Title,
                    Title = repo.Key,
                    Url = $"https://github.com/{repo.Value.Title}",
                    Description = repo.Value.Desc,
                    Stars = repo.Value.Stars,
                    Language = "C#"
                });
            }

            return await Task.FromResult(results);
        }

        public async Task<List<DocumentationResult>> SearchDocumentationAsync(string appFramework)
        {
            var results = new List<DocumentationResult>();

            var frameworkDocs = new Dictionary<string, List<(string Title, string Url, string Summary)>>
            {
                { "WPF", new List<(string, string, string)>
                    {
                        ("UI Automation Overview", "https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/", "Microsoft UI Automation framework"),
                        ("AutomationElement Class", "https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement", "Core class for UI Automation"),
                    }
                },
                { "WinForms", new List<(string, string, string)>
                    {
                        ("Accessibility in WinForms", "https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/providing-accessibility-information", "Accessibility support for WinForms"),
                    }
                },
                { "Electron", new List<(string, string, string)>
                    {
                        ("Electron Testing", "https://www.electronjs.org/docs/latest/tutorial/automated-testing", "Automated testing for Electron apps"),
                    }
                },
            };

            if (frameworkDocs.TryGetValue(appFramework, out var docs))
            {
                foreach (var doc in docs)
                {
                    results.Add(new DocumentationResult
                    {
                        Title = doc.Title,
                        Url = doc.Url,
                        Summary = doc.Summary,
                        Source = "Microsoft Docs"
                    });
                }
            }

            return await Task.FromResult(results);
        }

        public List<string> SuggestPatterns(ResearchResult research)
        {
            var suggestions = new List<string>();

            switch (research.Framework)
            {
                case ApplicationFramework.WPF:
                    suggestions.Add("Use UIA3 automation for best WPF element discovery");
                    suggestions.Add("WPF controls support rich automation patterns (Value, Invoke, SelectionItem)");
                    suggestions.Add("Use AutomationId for reliable element identification");
                    break;
                case ApplicationFramework.WinForms:
                    suggestions.Add("Try UIA2 first for WinForms, fallback to UIA3");
                    suggestions.Add("WinForms controls may need MSAA accessibility bridge");
                    break;
                case ApplicationFramework.Electron:
                    suggestions.Add("Electron apps respond well to keyboard shortcuts");
                    suggestions.Add("Use vision-based detection as primary; UIA may not expose all elements");
                    suggestions.Add("Chrome DevTools Protocol may provide additional access");
                    break;
                case ApplicationFramework.UWP:
                    suggestions.Add("UWP apps have full UIA3 support");
                    suggestions.Add("Use AutomationProperties.AutomationId for element identification");
                    break;
                default:
                    suggestions.Add("Use vision-based approach as primary detection method");
                    suggestions.Add("Combine FlaUI element enumeration with OCR for text verification");
                    break;
            }

            foreach (var pattern in research.Patterns)
            {
                suggestions.Add($"Pattern: {pattern.Name} - {pattern.Description}");
            }

            return suggestions;
        }

        public ResearchResult? GetCachedResearch(string applicationName)
        {
            if (_cache.TryGetValue(applicationName, out var cached))
            {
                if (DateTime.UtcNow - cached.CachedAt < _cacheDuration)
                {
                    var result = cached.Result;
                    result.IsCached = true;
                    return result;
                }
                // Expired
                _cache.Remove(applicationName);
            }
            return null;
        }

        public void StoreApplicationPattern(string applicationName, AutomationPattern pattern)
        {
            if (!_knowledgeBase.ContainsKey(applicationName))
            {
                _knowledgeBase[applicationName] = new List<AutomationPattern>();
            }
            _knowledgeBase[applicationName].Add(pattern);
            SaveKnowledgeBase();
        }

        private List<AutomationPattern> GenerateDefaultPatterns(ApplicationFramework framework)
        {
            var patterns = new List<AutomationPattern>();

            switch (framework)
            {
                case ApplicationFramework.WPF:
                case ApplicationFramework.UWP:
                    patterns.Add(new AutomationPattern
                    {
                        Name = "InvokePattern",
                        Description = "Click buttons and similar controls",
                        Method = "element.Patterns.Invoke.Pattern.Invoke()",
                        Examples = new List<string> { "Button click", "Menu item selection" }
                    });
                    break;

                case ApplicationFramework.Electron:
                    patterns.Add(new AutomationPattern
                    {
                        Name = "KeyboardNavigation",
                        Description = "Use keyboard shortcuts for navigation",
                        Method = "SendShortcutAsync()",
                        Examples = new List<string> { "Ctrl+Tab for tab switching", "F5 for refresh" }
                    });
                    break;
            }

            return patterns;
        }

        private void CacheResult(string applicationName, ResearchResult result)
        {
            _cache[applicationName] = (result, DateTime.UtcNow);

            try
            {
                var cacheFile = Path.Combine(_cacheDir, $"{SanitizeFileName(applicationName)}.json");
                var json = JsonSerializer.Serialize(new { Result = result, CachedAt = DateTime.UtcNow }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(cacheFile, json);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ResearchAgent] Failed to cache result");
            }
        }

        private void LoadCachedResults()
        {
            try
            {
                if (!Directory.Exists(_cacheDir)) return;
                foreach (var file in Directory.GetFiles(_cacheDir, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        using var doc = JsonDocument.Parse(json);
                        var cachedAt = doc.RootElement.GetProperty("CachedAt").GetDateTime();

                        if (DateTime.UtcNow - cachedAt < _cacheDuration)
                        {
                            var resultJson = doc.RootElement.GetProperty("Result").GetRawText();
                            var result = JsonSerializer.Deserialize<ResearchResult>(resultJson);
                            if (result != null)
                            {
                                _cache[result.ApplicationName] = (result, cachedAt);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void LoadKnowledgeBase()
        {
            try
            {
                if (File.Exists(_knowledgeBasePath))
                {
                    var json = File.ReadAllText(_knowledgeBasePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, List<AutomationPattern>>>(json);
                    if (data != null)
                    {
                        foreach (var kvp in data)
                            _knowledgeBase[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ResearchAgent] Failed to load knowledge base");
            }
        }

        private void SaveKnowledgeBase()
        {
            try
            {
                var json = JsonSerializer.Serialize(_knowledgeBase, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_knowledgeBasePath, json);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ResearchAgent] Failed to save knowledge base");
            }
        }

        private string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // PInvoke
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
