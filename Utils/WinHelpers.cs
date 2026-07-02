using System;
using System.Diagnostics;
using JarvisCSharp.Core;

namespace JarvisCSharp.Utils
{
    public static class WinHelpers
    {
        public static void OpenUrl(string url)
        {
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("spotify:") && !url.StartsWith("whatsapp:"))
                {
                    url = "https://" + url.TrimStart('/');
                }

                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to open URL: {url}");
            }
        }
    }
}
