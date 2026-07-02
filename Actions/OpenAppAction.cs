using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Actions
{
    public class OpenAppAction : IAction
    {
        public string Name => "open_app";
        public string Description => "Opens an application by name or alias.";

        private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            {"edge", "msedge"}, {"chrome", "chrome"}, {"firefox", "firefox"},
            {"terminal", "cmd"}, {"cmd", "cmd"}, {"powershell", "powershell"},
            {"explorer", "explorer"}, {"spotify", "Spotify"}, {"vscode", "code"},
            {"code", "code"}, {"discord", "Update"}, {"whatsapp", "WhatsApp"},
            {"notepad", "notepad"}, {"word", "winword"}, {"excel", "excel"},
            {"powerpoint", "powerpnt"}, {"calculator", "calc"}, {"settings", "ms-settings:"},
            {"paint", "mspaint"}, {"taskmgr", "taskmgr"}, {"control", "control"},
            {"regedit", "regedit"}, {"steam", "steam"}, {"obs", "obs64"},
        };

        public async Task<string> ExecuteAsync(string payload)
        {
            var appName = payload.Trim();
            if (string.IsNullOrEmpty(appName)) return "Uygulama adı belirtilmedi.";

            var resolved = _aliases.TryGetValue(appName, out var alias) ? alias : appName;

            try
            {
                if (resolved.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase))
                {
                    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    var path = Path.Combine(localAppData, "WhatsApp", "WhatsApp.exe");
                    if (File.Exists(path))
                    {
                        Process.Start(path);
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo { FileName = "whatsapp:", UseShellExecute = true });
                    }
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = resolved, UseShellExecute = true });
                }

                Logger.Information($"[OpenApp] {resolved} başlatıldı, penceresi bekleniyor...");
                
                // Wait for the window to appear (up to 3 seconds)
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(200);
                    var windows = Services.Win32Interop.GetAllVisibleWindows();
                    var target = windows.FirstOrDefault(w => w.Title.Contains(appName, StringComparison.OrdinalIgnoreCase) || 
                                                             w.Title.Contains(resolved, StringComparison.OrdinalIgnoreCase));
                    if (target.Handle != IntPtr.Zero)
                    {
                        Services.Win32Interop.BringToFront(target.Handle);
                        return $"{appName} açıldı ve odaklandı.";
                    }
                }

                return $"{appName} başlatıldı (ancak penceresi anında bulunamadı, arka planda açılıyor olabilir).";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to open app: {resolved}");
                return $"Hata: {appName} açılamadı — {ex.Message}";
            }
        }
    }
}
