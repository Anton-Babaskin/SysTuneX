namespace SysTuneX.Core.Models;

/// <summary>
/// What the machine looked like at one moment, in the terms SysTuneX can actually observe.
///
/// Deliberately not a performance measurement. SysTuneX changes settings; it cannot see frame
/// times, input latency or anything a game would report. Comparing two of these answers "what
/// changed on this machine", which is a question it can answer honestly — the FPS question needs
/// a benchmark run, and claiming otherwise would be the thing that makes tuners untrustworthy.
/// </summary>
public sealed record SystemStateSnapshot
{
    public required string Id { get; init; }

    /// <summary>What the user, or the app, called this moment: "before Competitive FPS".</summary>
    public required string Label { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Ids of catalog tweaks reading as applied.</summary>
    public IReadOnlyList<string> AppliedTweakIds { get; init; } = [];

    /// <summary>Managed services that were running.</summary>
    public IReadOnlyList<string> RunningServices { get; init; } = [];

    public Guid PowerScheme { get; init; }

    public string PowerSchemeName { get; init; } = string.Empty;

    public long RamUsedMb { get; init; }

    public long RamTotalMb { get; init; }

    public long StandbyMb { get; init; }

    public int ProcessCount { get; init; }

    /// <summary>One CPU sample. Noisy by nature, which is why the comparison labels it as such.</summary>
    public double CpuUsagePercent { get; init; }
}

/// <param name="Category">Grouping for the interface: tweaks, services, power, memory.</param>
/// <param name="Item">What changed.</param>
/// <param name="Before">Value in the earlier snapshot, or null when the item did not exist then.</param>
/// <param name="After">Value in the later snapshot, or null when it is gone.</param>
public sealed record SnapshotChange(string Category, string Item, string? Before, string? After);

public sealed record SnapshotComparison(
    SystemStateSnapshot Before,
    SystemStateSnapshot After,
    IReadOnlyList<SnapshotChange> Changes)
{
    public bool HasChanges => Changes.Count > 0;
}
