namespace SysTuneX.Core.Models;

/// <summary>One sample of the live counters shown on the dashboard.</summary>
public sealed record SystemSnapshot
{
    public static readonly SystemSnapshot Empty = new();

    /// <summary>Total CPU load, 0-100.</summary>
    public double CpuUsagePercent { get; init; }

    public long RamUsedMb { get; init; }
    public long RamTotalMb { get; init; }

    public double RamUsagePercent => RamTotalMb > 0 ? RamUsedMb * 100.0 / RamTotalMb : 0;

    /// <summary>Pages the memory manager is holding as cache and can hand back on demand.</summary>
    public long StandbyMemoryMb { get; init; }

    public int ProcessCount { get; init; }
    public int ThreadCount { get; init; }
    public long SystemDriveFreeMb { get; init; }
    public long SystemDriveTotalMb { get; init; }
    public TimeSpan Uptime { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
