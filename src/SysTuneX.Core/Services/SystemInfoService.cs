using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Native;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="ISystemInfoService"/>
[SupportedOSPlatform("windows")]
public sealed class SystemInfoService : ISystemInfoService
{
    private const string DisplayAdapterClassKey =
        @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private readonly ILogger<SystemInfoService> _logger;
    private readonly IRegistryService _registry;
    private readonly IEnvironmentService _environment;
    private readonly SemaphoreSlim _hardwareGate = new(1, 1);

    private HardwareInfo? _cachedHardware;

    // Previous GetSystemTimes sample, used to turn the running totals into a percentage.
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private double _lastCpuUsage;

    public SystemInfoService(
        ILogger<SystemInfoService> logger,
        IRegistryService registry,
        IEnvironmentService environment)
    {
        _logger = logger;
        _registry = registry;
        _environment = environment;
    }

    public async Task<HardwareInfo> GetHardwareInfoAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedHardware is not null)
        {
            return _cachedHardware;
        }

        await _hardwareGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedHardware is not null)
            {
                return _cachedHardware;
            }

            // The WMI round-trips take a second or two; never let them run on the UI thread.
            _cachedHardware = await Task.Run(BuildHardwareInfo, cancellationToken).ConfigureAwait(false);
            return _cachedHardware;
        }
        finally
        {
            _hardwareGate.Release();
        }
    }

    public SystemSnapshot GetSnapshot()
    {
        try
        {
            long totalMb = 0;
            long availableMb = 0;
            if (NativeHelpers.TryGetMemoryStatus(out NativeMethods.MEMORYSTATUSEX memory))
            {
                // GlobalMemoryStatusEx reports installed physical memory. The old code used the
                // GC heap limit here, which is why the dashboard showed the wrong RAM size.
                totalMb = (long)(memory.TotalPhys / (1024 * 1024));
                availableMb = (long)(memory.AvailPhys / (1024 * 1024));
            }

            int processCount = 0;
            int threadCount = 0;
            if (NativeHelpers.TryGetPerformanceInfo(out NativeMethods.PERFORMANCE_INFORMATION perf))
            {
                processCount = perf.ProcessCount;
                threadCount = perf.ThreadCount;
            }

            (long driveFreeMb, long driveTotalMb) = GetSystemDriveSpace();

            return new SystemSnapshot
            {
                CpuUsagePercent = SampleCpuUsage(),
                RamTotalMb = totalMb,
                RamUsedMb = Math.Max(0, totalMb - availableMb),
                StandbyMemoryMb = NativeHelpers.GetStandbyBytes() / (1024 * 1024),
                ProcessCount = processCount,
                ThreadCount = threadCount,
                SystemDriveFreeMb = driveFreeMb,
                SystemDriveTotalMb = driveTotalMb,
                Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not build a system snapshot");
            return SystemSnapshot.Empty;
        }
    }

    /// <summary>
    /// Total CPU load from GetSystemTimes deltas. This replaces PerformanceCounter, which took
    /// about a second to construct, threw on machines with a damaged counter registry, and did
    /// so inside the DI graph so the whole app failed to start.
    /// </summary>
    private double SampleCpuUsage()
    {
        if (!NativeMethods.GetSystemTimes(
                out NativeMethods.FILETIME idle,
                out NativeMethods.FILETIME kernel,
                out NativeMethods.FILETIME user))
        {
            return _lastCpuUsage;
        }

        ulong idleTicks = idle.ToUInt64();
        ulong kernelTicks = kernel.ToUInt64();
        ulong userTicks = user.ToUInt64();

        if (_previousKernel == 0 && _previousUser == 0)
        {
            _previousIdle = idleTicks;
            _previousKernel = kernelTicks;
            _previousUser = userTicks;
            return 0;
        }

        // Kernel time already includes idle time, so total is kernel + user.
        ulong idleDelta = idleTicks - _previousIdle;
        ulong totalDelta = kernelTicks - _previousKernel + (userTicks - _previousUser);

        _previousIdle = idleTicks;
        _previousKernel = kernelTicks;
        _previousUser = userTicks;

        if (totalDelta == 0)
        {
            return _lastCpuUsage;
        }

        _lastCpuUsage = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
        return _lastCpuUsage;
    }

    private HardwareInfo BuildHardwareInfo()
    {
        (string cpuName, int cores, int threads) = QueryProcessor();
        (string gpuName, string driverVersion) = QueryPrimaryGpu();
        long vramMb = QueryDedicatedVideoMemoryMb(gpuName);

        long totalRamMb = NativeHelpers.TryGetMemoryStatus(out NativeMethods.MEMORYSTATUSEX memory)
            ? (long)(memory.TotalPhys / (1024 * 1024))
            : 0;

        return new HardwareInfo
        {
            CpuName = cpuName,
            CpuCores = cores,
            CpuThreads = threads,
            GpuName = gpuName,
            GpuVramMb = vramMb,
            GpuDriverVersion = driverVersion,
            RamTotalMb = totalRamMb,
            RamSummary = QueryMemoryModuleSummary(totalRamMb),
            MotherboardName = QueryMotherboard(),
            SystemDriveModel = QuerySystemDriveModel(),
            Windows = _environment.Windows,
        };
    }

    private (string Name, int Cores, int Threads) QueryProcessor()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    string name = item["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                    int cores = ToInt(item["NumberOfCores"], Environment.ProcessorCount);
                    int threads = ToInt(item["NumberOfLogicalProcessors"], Environment.ProcessorCount);
                    return (name, cores, threads);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Win32_Processor query failed");
        }

        return ("Unknown CPU", Environment.ProcessorCount, Environment.ProcessorCount);
    }

    /// <summary>
    /// Picks the real GPU rather than the first row WMI happens to return, which on hybrid
    /// laptops is usually the integrated adapter or the Basic Display Adapter fallback.
    /// </summary>
    private (string Name, string DriverVersion) QueryPrimaryGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");

            var candidates = new List<(string Name, string Driver, long Ram, int Score)>();

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    string name = item["Name"]?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    string driver = item["DriverVersion"]?.ToString() ?? string.Empty;
                    long ram = ToLong(item["AdapterRAM"], 0);

                    int score = 0;
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                    {
                        score += 100;
                    }

                    if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Meta Virtual", StringComparison.OrdinalIgnoreCase))
                    {
                        score -= 200;
                    }

                    candidates.Add((name, driver, ram, score));
                }
            }

            (string Name, string Driver, long Ram, int Score) best = candidates
                .OrderByDescending(c => c.Score)
                .ThenByDescending(c => c.Ram)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(best.Name))
            {
                return (best.Name, best.Driver);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Win32_VideoController query failed");
        }

        return ("Unknown GPU", string.Empty);
    }

    /// <summary>
    /// Reads VRAM from the display driver key. Win32_VideoController.AdapterRAM is a UINT32 and
    /// therefore wraps on any card with 4 GB or more, which covers every GPU worth tuning.
    /// </summary>
    private long QueryDedicatedVideoMemoryMb(string gpuName)
    {
        try
        {
            foreach (string subKey in _registry.GetSubKeyNames(DisplayAdapterClassKey))
            {
                if (!subKey.All(char.IsDigit))
                {
                    continue;
                }

                string path = $@"{DisplayAdapterClassKey}\{subKey}";
                string? description = _registry.GetValue(path, "DriverDesc") as string;
                if (description is null ||
                    (!string.IsNullOrEmpty(gpuName) &&
                     !description.Equals(gpuName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                object? memory = _registry.GetValue(path, "HardwareInformation.qwMemorySize");
                long bytes = memory switch
                {
                    long l => l,
                    int i => i,
                    byte[] raw when raw.Length >= 8 => BitConverter.ToInt64(raw, 0),
                    byte[] raw when raw.Length >= 4 => BitConverter.ToUInt32(raw, 0),
                    _ => 0,
                };

                if (bytes > 0)
                {
                    return bytes / (1024 * 1024);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read dedicated video memory");
        }

        return 0;
    }

    private string QueryMemoryModuleSummary(long totalRamMb)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");

            int moduleCount = 0;
            int topSpeed = 0;

            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    moduleCount++;
                    int speed = Math.Max(ToInt(item["ConfiguredClockSpeed"], 0), ToInt(item["Speed"], 0));
                    topSpeed = Math.Max(topSpeed, speed);
                }
            }

            double gigabytes = totalRamMb / 1024.0;
            string size = $"{gigabytes:0.#} GB";

            if (moduleCount > 0 && topSpeed > 0)
            {
                return $"{size} ({moduleCount}x, {topSpeed} MHz)";
            }

            return moduleCount > 0 ? $"{size} ({moduleCount}x)" : size;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Win32_PhysicalMemory query failed");
            return $"{totalRamMb / 1024.0:0.#} GB";
        }
    }

    private string QueryMotherboard()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    string manufacturer = item["Manufacturer"]?.ToString()?.Trim() ?? string.Empty;
                    string product = item["Product"]?.ToString()?.Trim() ?? string.Empty;
                    return $"{manufacturer} {product}".Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Win32_BaseBoard query failed");
        }

        return string.Empty;
    }

    private string QuerySystemDriveModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model, MediaType FROM Win32_DiskDrive WHERE Index = 0");
            foreach (ManagementBaseObject item in searcher.Get())
            {
                using (item)
                {
                    return item["Model"]?.ToString()?.Trim() ?? string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Win32_DiskDrive query failed");
        }

        return string.Empty;
    }

    private (long FreeMb, long TotalMb) GetSystemDriveSpace()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return (0, 0);
            }

            return (drive.AvailableFreeSpace / (1024 * 1024), drive.TotalSize / (1024 * 1024));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read system drive space");
            return (0, 0);
        }
    }

    private static int ToInt(object? value, int fallback)
    {
        try
        {
            return value is null ? fallback : Convert.ToInt32(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static long ToLong(object? value, long fallback)
    {
        try
        {
            return value is null ? fallback : Convert.ToInt64(value);
        }
        catch
        {
            return fallback;
        }
    }
}
