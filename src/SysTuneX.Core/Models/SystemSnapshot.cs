namespace SysTuneX.Core.Models;

public record SystemSnapshot
{
    public float CpuUsage { get; init; }
    public float GpuUsage { get; init; }
    public long RamUsedMb { get; init; }
    public long RamTotalMb { get; init; }
    public float CpuTempC { get; init; }
    public float GpuTempC { get; init; }
    public int RunningProcesses { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
