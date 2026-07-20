using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

public class SystemInfoService : ISystemInfoService
{
    private readonly ILogger<SystemInfoService> _logger;
    private readonly PerformanceCounter _cpuCounter;
    private HardwareInfo? _cachedInfo;

    public SystemInfoService(ILogger<SystemInfoService> logger)
    {
        _logger = logger;
        _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _cpuCounter.NextValue(); // first call always returns 0
    }

    public HardwareInfo GetHardwareInfo()
    {
        if (_cachedInfo != null) return _cachedInfo;

        try
        {
            string cpuName = "Unknown";
            int cores = Environment.ProcessorCount;
            int threads = Environment.ProcessorCount;

            using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, ThreadCount FROM Win32_Processor"))
            {
                foreach (var obj in searcher.Get())
                {
                    cpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    cores = Convert.ToInt32(obj["NumberOfCores"]);
                    threads = Convert.ToInt32(obj["ThreadCount"]);
                    break;
                }
            }

            string gpuName = "Unknown";
            long vram = 0;
            using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
            {
                foreach (var obj in searcher.Get())
                {
                    gpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    vram = Convert.ToInt64(obj["AdapterRAM"] ?? 0) / (1024 * 1024);
                    break;
                }
            }

            var gcInfo = GC.GetGCMemoryInfo();

            _cachedInfo = new HardwareInfo
            {
                CpuName = cpuName,
                CpuCores = cores,
                CpuThreads = threads,
                GpuName = gpuName,
                GpuVramMb = vram,
                RamTotalMb = (long)(gcInfo.TotalAvailableMemoryBytes / (1024.0 * 1024.0)),
                OsVersion = Environment.OSVersion.VersionString,
                OsBuild = Environment.OSVersion.Version.Build.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get hardware info");
            _cachedInfo = new HardwareInfo();
        }

        return _cachedInfo;
    }

    public SystemSnapshot GetCurrentSnapshot()
    {
        try
        {
            var cpuUsage = _cpuCounter.NextValue();
            var gcInfo = GC.GetGCMemoryInfo();
            var totalRam = gcInfo.TotalAvailableMemoryBytes / (1024 * 1024);

            long usedRam = 0;
            using (var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem"))
            {
                foreach (var obj in searcher.Get())
                {
                    var freeKb = Convert.ToInt64(obj["FreePhysicalMemory"] ?? 0);
                    usedRam = totalRam - (freeKb / 1024);
                    break;
                }
            }

            return new SystemSnapshot
            {
                CpuUsage = cpuUsage,
                RamUsedMb = usedRam,
                RamTotalMb = totalRam,
                RunningProcesses = Process.GetProcesses().Length,
                Timestamp = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get system snapshot");
            return new SystemSnapshot();
        }
    }
}
