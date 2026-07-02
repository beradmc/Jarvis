using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services
{
    public class SystemShellService
    {
        public SystemShellService()
        {
        }

        /// <summary>
        /// Executes a PowerShell command silently in the background.
        /// </summary>
        /// <param name="command">The PowerShell command string to execute.</param>
        /// <returns>The standard output of the command.</returns>
        public async Task<string> ExecutePowerShellCommandAsync(string command)
        {
            return await ExecuteCommandAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"");
        }

        /// <summary>
        /// Executes a CMD command silently in the background.
        /// </summary>
        /// <param name="command">The CMD command string to execute.</param>
        /// <returns>The standard output of the command.</returns>
        public async Task<string> ExecuteCmdCommandAsync(string command)
        {
            return await ExecuteCommandAsync("cmd.exe", $"/c {command}");
        }

        private async Task<string> ExecuteCommandAsync(string fileName, string arguments)
        {
            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true, // Ensure no black console window pops up
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = new Process { StartInfo = processStartInfo };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null) outputBuilder.AppendLine(args.Data);
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null) errorBuilder.AppendLine(args.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                string output = outputBuilder.ToString().Trim();
                string error = errorBuilder.ToString().Trim();

                if (!string.IsNullOrEmpty(error))
                {
                    Logger.Warning($"Command '{fileName} {arguments}' produced error output: {error}");
                }

                return output;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to execute command: {fileName} {arguments}");
                return $"Error: {ex.Message}";
            }
        }
    }
}
