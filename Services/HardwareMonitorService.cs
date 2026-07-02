using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using JarvisCSharp.Core;
using Microsoft.Win32;

namespace JarvisCSharp.Services
{
    public class HardwareMonitorService : IDisposable
    {
        public HardwareMonitorService()
        {
            // Subscribe to system events
            SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        }

        private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    Logger.Information("System is going to sleep/suspend.");
                    // Optional: trigger event in memory or main loop
                    break;
                case PowerModes.Resume:
                    Logger.Information("System has resumed from sleep/suspend.");
                    // Optional: trigger event in memory or main loop
                    break;
            }
        }

        /// <summary>
        /// Gets the total amount of free physical memory (RAM) in Megabytes.
        /// </summary>
        public async Task<ulong> GetFreePhysicalMemoryMBAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (ulong.TryParse(obj["FreePhysicalMemory"]?.ToString(), out ulong freeKb))
                        {
                            return freeKb / 1024;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to get free physical memory via WMI.");
                }
                return 0UL;
            });
        }

        /// <summary>
        /// Gets current CPU load percentage.
        /// </summary>
        public async Task<float> GetCpuLoadPercentageAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT LoadPercentage FROM Win32_Processor");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (float.TryParse(obj["LoadPercentage"]?.ToString(), out float load))
                        {
                            return load;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to get CPU load via WMI.");
                }
                return 0f;
            });
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        }
    }
}
