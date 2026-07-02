using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using JarvisCSharp.Core;

namespace JarvisCSharp.Services
{
    public class SystemInfoService
    {
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _ramCounter;

        public SystemInfoService()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _cpuCounter.NextValue(); // First call returns 0
                    
                    _ramCounter = new PerformanceCounter("Memory", "% Committed Bytes In Use");
                }
                Logger.Information("SystemInfoService initialized");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize performance counters");
            }
        }

        public float GetCpuUsage()
        {
            try
            {
                return _cpuCounter?.NextValue() ?? 0f;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get CPU usage");
                return 0f;
            }
        }

        public float GetRamUsage()
        {
            try
            {
                return _ramCounter?.NextValue() ?? 0f;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get RAM usage");
                return 0f;
            }
        }

        public float GetBatteryLevel()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    GetSystemPowerStatus(out var status);
                    return status.BatteryLifePercentValue;
                }
                return 100f;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get battery level");
                return 100f;
            }
        }

        public string GetSystemInfo(string query)
        {
            return query.ToLower() switch
            {
                "cpu" => $"CPU Usage: {GetCpuUsage():F1}%",
                "ram" => $"RAM Usage: {GetRamUsage():F1}%",
                "battery" => $"Battery: {GetBatteryLevel():F1}%",
                "time" => $"Time: {DateTime.Now:HH:mm:ss}",
                "date" => $"Date: {DateTime.Now:dddd, dd MMMM yyyy}",
                "all" => GetFullSystemInfo(),
                _ => "Unknown query"
            };
        }

        private string GetFullSystemInfo()
        {
            return $"CPU: {GetCpuUsage():F1}% | RAM: {GetRamUsage():F1}% | Battery: {GetBatteryLevel():F1}%";
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus sps);

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;

            public float BatteryLifePercentValue => BatteryLifePercent != 255 ? BatteryLifePercent : 100f;
        }
    }
}
