namespace SysTuneX.Core.Models;

public record HardwareInfo
{
    public string CpuName { get; init; } = "Unknown";
    public int CpuCores { get; init; }
    public int CpuThreads { get; init; }
    public string GpuName { get; init; } = "Unknown";
    public long GpuVramMb { get; init; }
    public long RamTotalMb { get; init; }
    public string OsVersion { get; init; } = "Unknown";
    public string OsBuild { get; init; } = "Unknown";
}
