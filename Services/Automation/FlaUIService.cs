using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA2;
using FlaUI.UIA3;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    public class AutomationElementInfo
    {
        public string Name { get; set; } = string.Empty;
        public string AutomationId { get; set; } = string.Empty;
        public ControlType Type { get; set; }
        public Rectangle BoundingRectangle { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsVisible { get; set; }
        public AutomationElement NativeElement { get; set; } = null!;
        public List<string> SupportedPatterns { get; set; } = new List<string>();
    }

    /// <summary>
    /// Wrapper around FlaUI library for Windows UI Automation framework access.
    /// </summary>
    public class FlaUIService : IDisposable
    {
        private readonly UIA3Automation _uia3;
        private readonly UIA2Automation _uia2;
        private readonly ConcurrentDictionary<IntPtr, (DateTime Expiry, List<AutomationElementInfo> Elements)> _elementCache;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(5);

        public FlaUIService()
        {
            _uia3 = new UIA3Automation();
            _uia2 = new UIA2Automation();
            _elementCache = new ConcurrentDictionary<IntPtr, (DateTime, List<AutomationElementInfo>)>();
        }

        public void InvalidateCache(IntPtr windowHandle)
        {
            _elementCache.TryRemove(windowHandle, out _);
        }

        public List<AutomationElementInfo>? GetCachedElements(IntPtr windowHandle)
        {
            if (_elementCache.TryGetValue(windowHandle, out var cache))
            {
                if (DateTime.UtcNow < cache.Expiry)
                {
                    return cache.Elements;
                }
                InvalidateCache(windowHandle);
            }
            return null;
        }

        public List<AutomationElementInfo> EnumerateElements(IntPtr windowHandle)
        {
            var cached = GetCachedElements(windowHandle);
            if (cached != null) return cached;

            var elements = new List<AutomationElementInfo>();
            
            try
            {
                AutomationElement window;
                try
                {
                    window = _uia3.FromHandle(windowHandle);
                }
                catch
                {
                    Logger.Information($"[FlaUIService] UIA3 failed for handle {windowHandle}, falling back to UIA2.");
                    window = _uia2.FromHandle(windowHandle);
                }

                if (window == null) return elements;

                var descendants = window.FindAllDescendants();
                foreach (var element in descendants)
                {
                    try
                    {
                        if (element.Properties.IsOffscreen.IsSupported && element.IsOffscreen) continue;
                        if (element.Properties.IsEnabled.IsSupported && !element.IsEnabled) continue;

                        var info = new AutomationElementInfo
                        {
                            Name = element.Properties.Name.IsSupported ? element.Name : string.Empty,
                            AutomationId = element.Properties.AutomationId.IsSupported ? element.AutomationId : string.Empty,
                            Type = element.Properties.ControlType.IsSupported ? element.ControlType : ControlType.Custom,
                            BoundingRectangle = element.Properties.BoundingRectangle.IsSupported ? element.BoundingRectangle : Rectangle.Empty,
                            IsEnabled = true,
                            IsVisible = true,
                            NativeElement = element
                        };
                        
                        // Discover patterns
                        var patterns = new List<string>();
                        if (element.Patterns.Invoke.IsSupported) patterns.Add("Invoke");
                        if (element.Patterns.Value.IsSupported) patterns.Add("Value");
                        if (element.Patterns.SelectionItem.IsSupported) patterns.Add("SelectionItem");
                        if (element.Patterns.Toggle.IsSupported) patterns.Add("Toggle");
                        if (element.Patterns.ExpandCollapse.IsSupported) patterns.Add("ExpandCollapse");
                        info.SupportedPatterns = patterns;

                        elements.Add(info);
                    }
                    catch
                    {
                        // Ignore elements throwing exceptions during enumeration
                    }
                }

                _elementCache[windowHandle] = (DateTime.UtcNow.Add(_cacheDuration), elements);
                return elements;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[FlaUIService] Enumeration failed for handle {windowHandle}");
                return elements;
            }
        }

        public AutomationElementInfo? FindElement(IntPtr windowHandle, string nameOrId, ControlType? controlType = null)
        {
            var elements = EnumerateElements(windowHandle);
            
            var query = elements.AsQueryable();
            if (controlType.HasValue)
            {
                query = query.Where(e => e.Type == controlType.Value);
            }

            // Exact match
            var exact = query.FirstOrDefault(e => 
                string.Equals(e.Name, nameOrId, StringComparison.OrdinalIgnoreCase) || 
                string.Equals(e.AutomationId, nameOrId, StringComparison.OrdinalIgnoreCase));
            
            if (exact != null) return exact;

            // Partial match
            return query.FirstOrDefault(e => 
                (e.Name != null && e.Name.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (e.AutomationId != null && e.AutomationId.IndexOf(nameOrId, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        public async Task<bool> ClickElementAsync(AutomationElementInfo elementInfo)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var element = elementInfo.NativeElement;
                    if (element.Patterns.Invoke.IsSupported)
                    {
                        element.Patterns.Invoke.Pattern.Invoke();
                        return true;
                    }
                    else if (element.Patterns.SelectionItem.IsSupported)
                    {
                        element.Patterns.SelectionItem.Pattern.Select();
                        return true;
                    }
                    else if (element.Patterns.Toggle.IsSupported)
                    {
                        element.Patterns.Toggle.Pattern.Toggle();
                        return true;
                    }
                    
                    // Fallback to Click
                    element.Click();
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"[FlaUIService] Failed to click element: {elementInfo.Name}");
                    return false;
                }
            });
        }

        public async Task<bool> TypeIntoElementAsync(AutomationElementInfo elementInfo, string text)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var element = elementInfo.NativeElement;
                    element.Focus();
                    
                    if (element.Patterns.Value.IsSupported && !element.Patterns.Value.Pattern.IsReadOnly)
                    {
                        element.Patterns.Value.Pattern.SetValue(text);
                        return true;
                    }

                    // Fallback using keyboard
                    element.Click();
                    System.Threading.Thread.Sleep(50);
                    FlaUI.Core.Input.Keyboard.Type(text);
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"[FlaUIService] Failed to type into element: {elementInfo.Name}");
                    return false;
                }
            });
        }

        public Dictionary<string, object> GetElementProperties(AutomationElementInfo elementInfo)
        {
            var props = new Dictionary<string, object>
            {
                { "Name", elementInfo.Name },
                { "AutomationId", elementInfo.AutomationId },
                { "ControlType", elementInfo.Type.ToString() },
                { "BoundingRectangle", elementInfo.BoundingRectangle }
            };

            var element = elementInfo.NativeElement;
            try
            {
                if (element.Patterns.Value.IsSupported)
                {
                    props["Value"] = element.Patterns.Value.Pattern.Value.Value;
                }
                if (element.Patterns.Toggle.IsSupported)
                {
                    props["ToggleState"] = element.Patterns.Toggle.Pattern.ToggleState.Value.ToString();
                }
                if (element.Patterns.SelectionItem.IsSupported)
                {
                    props["IsSelected"] = element.Patterns.SelectionItem.Pattern.IsSelected.Value;
                }
            }
            catch { }

            return props;
        }

        public void Dispose()
        {
            _uia3?.Dispose();
            _uia2?.Dispose();
        }
    }
}
