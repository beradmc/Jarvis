using System;
using System.Linq;
using System.Threading.Tasks;
using JarvisCSharp.Core;
using JarvisCSharp.Services;
using Microsoft.Extensions.Logging;

namespace JarvisCSharp.Actions
{
    public class WindowControlAction : IAction
    {
        public string Name => "window_control";
        public string Description => "Pencere yönetimi: list, focus, close.";

        public Task<string> ExecuteAsync(string payload)
        {
            // Parse JSON if payload is a JSON string from ActionManager
            string actualPayload = payload;
            try
            {
                if (payload.TrimStart().StartsWith("{"))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(payload);
                    if (doc.RootElement.TryGetProperty("payload", out var prop))
                    {
                        actualPayload = prop.GetString() ?? "";
                    }
                }
            }
            catch { }

            var parts = actualPayload.Split(':', 2);
            var action = parts[0].Trim().ToLower();
            var value = parts.Length > 1 ? parts[1].Trim() : "";

            try
            {
                var result = action switch
                {
                    "list" => ListWindows(),
                    "focus" => FocusWindow(value),
                    "close" => CloseWindow(value),
                    _ => $"Bilinmeyen eylem: {action}"
                };

                Logger.Information($"[Window Control] {action} → {result.Split('\n')[0]}");
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Window action failed: {action}");
                return Task.FromResult($"Hata: {action} başarısız — {ex.Message}");
            }
        }

        private string ListWindows()
        {
            var windows = Win32Interop.GetAllVisibleWindows();
            if (windows.Count == 0) return "Açık pencere bulunamadı.";

            var listStr = string.Join("\n", windows.Select(w => $"- {w.Title}"));
            return $"Açık Pencereler:\n{listStr}";
        }

        private string FocusWindow(string titleQuery)
        {
            if (string.IsNullOrWhiteSpace(titleQuery)) return "Odaklanacak pencere adı belirtilmedi.";

            var windows = Win32Interop.GetAllVisibleWindows();
            var target = windows.FirstOrDefault(w => w.Title.Contains(titleQuery, StringComparison.OrdinalIgnoreCase));

            if (target.Handle != IntPtr.Zero)
            {
                Win32Interop.BringToFront(target.Handle);
                return $"'{target.Title}' penceresi öne getirildi.";
            }

            return $"'{titleQuery}' adında bir pencere bulunamadı.";
        }

        private string CloseWindow(string titleQuery)
        {
            if (string.IsNullOrWhiteSpace(titleQuery)) return "Kapatılacak pencere adı belirtilmedi.";

            var windows = Win32Interop.GetAllVisibleWindows();
            var target = windows.FirstOrDefault(w => w.Title.Contains(titleQuery, StringComparison.OrdinalIgnoreCase));

            if (target.Handle != IntPtr.Zero)
            {
                Win32Interop.BringToFront(target.Handle);
                System.Threading.Thread.Sleep(100);
                var sim = new WindowsInput.InputSimulator();
                sim.Keyboard.ModifiedKeyStroke(WindowsInput.Native.VirtualKeyCode.LMENU, WindowsInput.Native.VirtualKeyCode.F4);
                return $"'{target.Title}' penceresine kapatma sinyali gönderildi.";
            }

            return $"'{titleQuery}' adında bir pencere bulunamadı.";
        }
    }
}
