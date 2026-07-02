using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using JarvisCSharp.Services;

namespace JarvisCSharp.Actions
{
    public class SysInfoAction : IAction
    {
        public string Name => "sys_info";
        public string Description => "Retrieves system information: CPU, RAM, battery, time, date, disk, network.";

        private readonly SystemInfoService _sysInfoService;

        public SysInfoAction(SystemInfoService sysInfoService)
        {
            _sysInfoService = sysInfoService;
        }

        public Task<string> ExecuteAsync(string payload)
        {
            var query = (payload ?? "all").Trim().ToLower();
            try
            {
                var result = query switch
                {
                    "cpu"     => $"CPU kullanımı: %{_sysInfoService.GetCpuUsage():F1}",
                    "ram"     => $"RAM kullanımı: %{_sysInfoService.GetRamUsage():F1}",
                    "battery" => GetBattery(),
                    "time"    => $"Şu anki saat: {DateTime.Now:HH:mm:ss}",
                    "date"    => $"Bugünün tarihi: {DateTime.Now:dddd, dd MMMM yyyy}",
                    "disk"    => GetDiskInfo(),
                    "network" => GetNetworkInfo(),
                    _         => GetAll()
                };
                Core.Logger.Information($"[SysInfo] {result}");
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                Core.Logger.Error(ex, "SysInfo failed");
                return Task.FromResult($"Hata: Sistem bilgisi alınamadı — {ex.Message}");
            }
        }

        private string GetBattery()
        {
            var level = _sysInfoService.GetBatteryLevel();
            return level >= 100
                ? "Pil: Tam dolu veya fişe takılı"
                : $"Pil: %{level:F0}";
        }

        private static string GetDiskInfo()
        {
            try
            {
                var drives = System.IO.DriveInfo.GetDrives();
                var parts = new System.Collections.Generic.List<string>();
                foreach (var d in drives)
                {
                    if (!d.IsReady) continue;
                    var freeGb  = d.AvailableFreeSpace / 1_073_741_824.0;
                    var totalGb = d.TotalSize / 1_073_741_824.0;
                    parts.Add($"{d.Name} {freeGb:F1}GB boş / {totalGb:F1}GB toplam");
                }
                return parts.Count > 0 ? "Disk: " + string.Join(", ", parts) : "Disk bilgisi alınamadı.";
            }
            catch { return "Disk bilgisi alınamadı."; }
        }

        private static string GetNetworkInfo()
        {
            try
            {
                bool connected = false;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.OperationalStatus == OperationalStatus.Up) { connected = true; break; }
                }
                return connected ? "Ağ bağlantısı: Aktif" : "Ağ bağlantısı: Yok";
            }
            catch { return "Ağ bilgisi alınamadı."; }
        }

        private string GetAll()
        {
            return string.Join(" | ", new[]
            {
                $"CPU: %{_sysInfoService.GetCpuUsage():F1}",
                $"RAM: %{_sysInfoService.GetRamUsage():F1}",
                GetBattery(),
                $"Saat: {DateTime.Now:HH:mm}",
                GetDiskInfo(),
                GetNetworkInfo()
            });
        }
    }
}
