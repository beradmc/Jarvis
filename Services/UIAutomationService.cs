using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services
{
    public class UIAutomationService : IDisposable
    {
        private readonly UIA3Automation _automation;

        public UIAutomationService()
        {
            _automation = new UIA3Automation();
        }

        /// <summary>
        /// Gets the root element of the currently active foreground window.
        /// </summary>
        private AutomationElement? GetActiveWindowElement()
        {
            var handle = Win32Interop.GetForegroundWindow();
            if (handle == IntPtr.Zero) return null;

            try
            {
                return _automation.FromHandle(handle);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to get automation element from foreground window handle: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Analyzes the active window and returns a list of actionable UI elements.
        /// </summary>
        public async Task<string> AnalyzeActiveWindowAsync()
        {
            return await Task.Run(() =>
            {
                var window = GetActiveWindowElement();
                if (window == null) return "Aktif pencere bulunamadı.";

                try
                {
                    var elements = window.FindAllDescendants(cf => 
                        cf.ByControlType(ControlType.Button).Or(
                        cf.ByControlType(ControlType.Edit)).Or(
                        cf.ByControlType(ControlType.MenuItem)).Or(
                        cf.ByControlType(ControlType.TabItem)).Or(
                        cf.ByControlType(ControlType.ListItem)).Or(
                        cf.ByControlType(ControlType.Text)).Or(
                        cf.ByControlType(ControlType.Hyperlink)).Or(
                        cf.ByControlType(ControlType.Document)));

                    var list = elements.Take(150).Select(e => 
                    {
                        string name = "";
                        try
                        {
                            if (e.Properties.Name.IsSupported) name = e.Name;
                            if (string.IsNullOrEmpty(name) && e.Properties.AutomationId.IsSupported) 
                                name = e.AutomationId;
                        }
                        catch { }
                        
                        return $"[{e.ControlType}] {name}";
                    }).Where(x => !x.EndsWith(" ]") && !x.EndsWith("] ")).ToList(); // Filter out empty names

                    return $"Aktif Pencere: {window.Name}\nBulunan Öğeler:\n{string.Join("\n", list)}";
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to analyze window.");
                    return $"Analiz hatası: {ex.Message}";
                }
            });
        }

        /// <summary>
        /// Finds a UI element by its Name or AutomationId in the active window.
        /// </summary>
        private AutomationElement? FindElementByName(AutomationElement root, string name)
        {
            var conditionFactory = _automation.ConditionFactory;
            var element = root.FindFirstDescendant(conditionFactory.ByName(name)) 
                       ?? root.FindFirstDescendant(conditionFactory.ByAutomationId(name));
                       
            if (element == null)
            {
                // Try case-insensitive and partial match fallback
                var all = root.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)
                    .Or(cf.ByControlType(ControlType.Edit))
                    .Or(cf.ByControlType(ControlType.MenuItem))
                    .Or(cf.ByControlType(ControlType.TabItem))
                    .Or(cf.ByControlType(ControlType.ListItem))
                    .Or(cf.ByControlType(ControlType.Text))
                    .Or(cf.ByControlType(ControlType.Hyperlink))
                    .Or(cf.ByControlType(ControlType.Document)));
                    
                element = all.FirstOrDefault(e => 
                {
                    try
                    {
                        string? eName = e.Properties.Name.IsSupported ? e.Name : null;
                        string? eId = e.Properties.AutomationId.IsSupported ? e.AutomationId : null;
                        
                        return (eName?.Equals(name, StringComparison.OrdinalIgnoreCase) == true) ||
                               (eName?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (eId?.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    catch
                    {
                        return false;
                    }
                });
            }

            return element;
        }

        /// <summary>
        /// Clicks a UI element by its name in the active window.
        /// </summary>
        public async Task<string> ClickElementByNameAsync(string elementName)
        {
            return await Task.Run(() =>
            {
                var window = GetActiveWindowElement();
                if (window == null) return "Aktif pencere bulunamadı.";

                try
                {
                    var element = FindElementByName(window, elementName);
                    if (element == null) return $"'{elementName}' isimli öğe bulunamadı.";

                    // Ensure window is in foreground
                    try { window.Focus(); } catch { }
                    System.Threading.Thread.Sleep(100);

                    // Try Invoke Pattern (Standard Button Click)
                    if (element.Patterns.Invoke.IsSupported)
                    {
                        element.Patterns.Invoke.Pattern.Invoke();
                        return $"'{elementName}' öğesine tıklandı (Invoke).";
                    }

                    // Fallback to Mouse Click (using bounding rectangle)
                    element.Click();
                    return $"'{elementName}' öğesine tıklandı (Fare ile).";
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to click element: {elementName}");
                    return $"Tıklama hatası: {ex.Message}";
                }
            });
        }

        /// <summary>
        /// Types text into a specific edit control by its name.
        /// </summary>
        public async Task<string> TypeTextIntoElementAsync(string elementName, string text)
        {
            return await Task.Run(() =>
            {
                var window = GetActiveWindowElement();
                if (window == null) return "Aktif pencere bulunamadı.";

                try
                {
                    var element = FindElementByName(window, elementName);
                    if (element == null) return $"'{elementName}' isimli öğe bulunamadı.";

                    try { window.Focus(); } catch { }
                    try { element.Focus(); } catch { }
                    System.Threading.Thread.Sleep(100);

                    if (element.Patterns.Value.IsSupported)
                    {
                        element.Patterns.Value.Pattern.SetValue(text);
                        return $"'{elementName}' içine metin yazıldı.";
                    }

                    // Fallback: Type using keyboard simulation
                    try { element.Click(); } catch { }
                    System.Threading.Thread.Sleep(100);
                    FlaUI.Core.Input.Keyboard.Type(text);
                    return $"'{elementName}' öğesine tıklanıp metin yazıldı (Klavye).";
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to type into element: {elementName}");
                    return $"Yazma hatası: {ex.Message}";
                }
            });
        }

        public void Dispose()
        {
            _automation?.Dispose();
        }
    }
}
