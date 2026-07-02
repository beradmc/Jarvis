using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Actions
{
    public class ShellAction : IAction
    {
        public string Name => "shell_run";
        public string Description => "Runs a Windows terminal command and returns output.";

        private static readonly string[] BlockList = {
            "format", "del /f", "rd /s", "rmdir /s", "shutdown", "reg delete",
            "cipher /w", "diskpart", "bcdedit", "sfc /scannow"
        };

        public async Task<string> ExecuteAsync(string payload)
        {
            var cmd = payload.Trim();
            if (string.IsNullOrEmpty(cmd)) return "Komut boş.";

            // Tehlikeli komut kontrolü
            var lower = cmd.ToLower();
            foreach (var blocked in BlockList)
            {
                if (lower.Contains(blocked))
                {
                    Logger.Warning($"[Shell] Blocked dangerous command: {cmd}");
                    return $"Güvenlik: Bu komut engellendi — {cmd}";
                }
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "cmd.exe",
                    Arguments              = $"/c {cmd}",
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding  = Encoding.UTF8,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };

                using var process = Process.Start(psi);
                if (process == null) return "Hata: Process başlatılamadı.";

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask  = process.StandardError.ReadToEndAsync();

                var completed = await Task.WhenAny(
                    Task.WhenAll(outputTask, errorTask),
                    Task.Delay(15000));

                if (!process.HasExited) process.Kill();

                var output = outputTask.IsCompleted ? outputTask.Result.Trim() : "";
                var error  = errorTask.IsCompleted  ? errorTask.Result.Trim()  : "";

                Logger.Information($"[Shell] cmd={cmd} out={output[..Math.Min(100, output.Length)]}");

                if (!string.IsNullOrEmpty(error) && process.ExitCode != 0)
                    return $"Hata ({process.ExitCode}): {error}";
                if (!string.IsNullOrEmpty(output))
                    return output;
                return "Komut çalıştırıldı (çıktı yok).";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Shell failed: {cmd}");
                return $"Hata: Komut başarısız — {ex.Message}";
            }
        }
    }
}
