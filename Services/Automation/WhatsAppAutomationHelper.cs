using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services.Automation
{
    public class WhatsAppAutomationHelper
    {
        private readonly AutomationControllerService _automation;
        private readonly FlaUIService _flaui;
        private readonly VisionEngineService _vision;

        public WhatsAppAutomationHelper(
            AutomationControllerService automation,
            FlaUIService flaui,
            VisionEngineService vision)
        {
            _automation = automation;
            _flaui = flaui;
            _vision = vision;
        }

        public async Task<ExecutionResult> OpenWhatsAppAsync()
        {
            try
            {
                // Try to open WhatsApp Desktop app first
                var launchRes = await _automation.LaunchApplicationAsync("whatsapp");
                if (launchRes.Success)
                {
                    Logger.Information("[WhatsAppAutomationHelper] Opened WhatsApp Desktop.");
                    await Task.Delay(3000); // Wait for load
                    return new ExecutionResult { Success = true, Message = "WhatsApp Desktop açıldı." };
                }

                // Fallback to Web
                Logger.Information("[WhatsAppAutomationHelper] Desktop failed, falling back to Web.");
                var url = "https://web.whatsapp.com";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                await Task.Delay(5000); // Wait for load
                return new ExecutionResult { Success = true, Message = "WhatsApp Web açıldı." };
            }
            catch (Exception ex)
            {
                return new ExecutionResult { Success = false, Message = $"WhatsApp açılamadı: {ex.Message}" };
            }
        }

        public async Task<ExecutionResult> CheckForQrCodeAsync()
        {
            // Simple vision check for QR code
            var visionRes = await _vision.AnalyzeTargetAsync("Ekranda WhatsApp QR kodu var mı?", "ActiveWindow");
            if (visionRes.AnalysisText.Contains("evet", StringComparison.OrdinalIgnoreCase) || 
                visionRes.AnalysisText.Contains("yes", StringComparison.OrdinalIgnoreCase) ||
                visionRes.AnalysisText.Contains("QR"))
            {
                return new ExecutionResult { Success = false, Message = "WhatsApp'a giriş yapılmamış. Lütfen telefonunuzdan QR kodu okutun." };
            }
            return new ExecutionResult { Success = true, Message = "QR kod yok, giriş yapılmış." };
        }

        public async Task<ExecutionResult> FindContactAsync(string contactName)
        {
            // Focus WhatsApp window first
            var proc = Process.GetProcesses().FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle) && p.MainWindowTitle.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase));
            if (proc != null)
            {
                await _automation.BringWindowToFocusAsync(proc.MainWindowHandle);
                await Task.Delay(500);
            }

            // Click search box. Shortcut for search is Ctrl+F in WhatsApp
            await _automation.SendShortcutAsync("Ctrl+F");
            await Task.Delay(500);
            
            // Type contact name
            await _automation.TypeTextAsync(contactName, null, true);
            await Task.Delay(1000); // wait for search results
            
            // Press Enter to select the first result
            var enterRes = await _automation.SendKeySequenceAsync(new[] { "Return" });
            if (!enterRes.Success) return enterRes;
            
            await Task.Delay(500);
            return new ExecutionResult { Success = true, Message = $"{contactName} bulundu ve seçildi." };
        }

        public async Task<ExecutionResult> SendMessageAsync(string contactName, string messageText)
        {
            // Ensure WhatsApp is open
            var proc = Process.GetProcesses().FirstOrDefault(p => !string.IsNullOrEmpty(p.MainWindowTitle) && p.MainWindowTitle.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase));
            if (proc == null)
            {
                var openRes = await OpenWhatsAppAsync();
                if (!openRes.Success) return openRes;
            }

            // Check for QR
            var qrRes = await CheckForQrCodeAsync();
            if (!qrRes.Success) return qrRes;

            // Find contact
            if (!string.IsNullOrEmpty(contactName))
            {
                var findRes = await FindContactAsync(contactName);
                if (!findRes.Success) return findRes;
            }

            // At this point, the chat is open and the message box should be focused.
            // Type message
            await _automation.TypeTextAsync(messageText, null, false);
            await Task.Delay(300);

            // Press Enter to send
            var sendRes = await _automation.SendKeySequenceAsync(new[] { "Return" });
            if (!sendRes.Success) return sendRes;

            return new ExecutionResult { Success = true, Message = "Mesaj gönderildi." };
        }

        public async Task<int> GetUnreadCountAsync()
        {
            var visionRes = await _vision.AnalyzeTargetAsync("Ekranda kaç tane okunmamış mesaj bildirim rozeti (yeşil veya mavi daire içinde sayı) var? Sadece sayıyı yaz.", "ActiveWindow");
            if (int.TryParse(visionRes.AnalysisText.Trim(), out int count))
            {
                return count;
            }
            return 0; // Default if not parsed
        }
    }
}
