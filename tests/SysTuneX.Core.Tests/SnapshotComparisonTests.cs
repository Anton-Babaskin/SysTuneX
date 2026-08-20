using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Comparison is where a tuner is most tempted to lie, so these pin the two properties that keep
/// it honest: it reports what actually entered and left, and it stays quiet about numbers that
/// move on their own.
/// </summary>
public sealed class SnapshotComparisonTests
{
    [Fact]
    public void Identical_snapshots_report_nothing()
    {
        SystemStateSnapshot before = Snapshot("before");
        SystemStateSnapshot after = before with { Id = "b", Label = "after", CapturedAt = before.CapturedAt.AddMinutes(1) };

        SnapshotComparison comparison = Service().Compare(before, after);

        Assert.False(comparison.HasChanges);
        Assert.Empty(comparison.Changes);
    }

    [Fact]
    public void A_tweak_becoming_applied_is_named()
    {
        SystemStateSnapshot before = Snapshot("before", tweaks: ["game_bar_disable"]);
        SystemStateSnapshot after = Snapshot("after", tweaks: ["game_bar_disable", "nagle_disable"], minutesLater: 5);

        SnapshotChange change = Assert.Single(Service().Compare(before, after).Changes);

        Assert.Equal("Tweak", change.Category);
        Assert.Equal("nagle_disable", change.Item);
        Assert.Equal("not applied", change.Before);
        Assert.Equal("applied", change.After);
    }

    [Fact]
    public void A_service_that_stopped_is_named()
    {
        SystemStateSnapshot before = Snapshot("before", services: ["DiagTrack", "SysMain"]);
        SystemStateSnapshot after = Snapshot("after", services: ["SysMain"], minutesLater: 5);

        SnapshotChange change = Assert.Single(Service().Compare(before, after).Changes);

        Assert.Equal("Service", change.Category);
        Assert.Equal("DiagTrack", change.Item);
        Assert.Equal("running", change.Before);
        Assert.Equal("stopped", change.After);
    }

    [Fact]
    public void A_changed_power_scheme_is_named_by_scheme_not_by_guid()
    {
        SystemStateSnapshot before = Snapshot("before") with
        {
            PowerScheme = PowerScheme.Balanced,
            PowerSchemeName = "Balanced",
        };

        SystemStateSnapshot after = Snapshot("after", minutesLater: 1) with
        {
            PowerScheme = PowerScheme.UltimatePerformance,
            PowerSchemeName = "Ultimate Performance",
        };

        SnapshotChange change = Assert.Single(Service().Compare(before, after).Changes);

        Assert.Equal("Power", change.Category);
        Assert.Equal("Balanced", change.Before);
        Assert.Equal("Ultimate Performance", change.After);
    }

    /// <summary>
    /// Memory drifts by tens of megabytes with nothing happening at all. Reporting that as an
    /// effect of a tweak would be data-shaped nonsense.
    /// </summary>
    [Fact]
    public void Memory_noise_is_not_reported_as_a_change()
    {
        SystemStateSnapshot before = Snapshot("before") with { RamUsedMb = 8000 };
        SystemStateSnapshot after = Snapshot("after", minutesLater: 1) with { RamUsedMb = 8100 };

        Assert.Empty(Service().Compare(before, after).Changes);
    }

    [Fact]
    public void A_real_memory_drop_is_reported()
    {
        SystemStateSnapshot before = Snapshot("before") with { RamUsedMb = 8000 };
        SystemStateSnapshot after = Snapshot("after", minutesLater: 1) with { RamUsedMb = 5500 };

        SnapshotChange change = Assert.Single(Service().Compare(before, after).Changes);

        Assert.Equal("Memory", change.Category);
        Assert.Equal("8000 MB", change.Before);
        Assert.Equal("5500 MB", change.After);
    }

    /// <summary>
    /// Picking the two the wrong way round in the interface should still read as "this became
    /// that" rather than inverting every line.
    /// </summary>
    [Fact]
    public void The_older_snapshot_is_always_the_before_side()
    {
        SystemStateSnapshot earlier = Snapshot("earlier", tweaks: []);
        SystemStateSnapshot later = Snapshot("later", tweaks: ["nagle_disable"], minutesLater: 10);

        SnapshotComparison comparison = Service().Compare(after: earlier, before: later);

        Assert.Equal("earlier", comparison.Before.Label);
        Assert.Equal("later", comparison.After.Label);

        SnapshotChange change = Assert.Single(comparison.Changes);
        Assert.Equal("applied", change.After);
    }

    private static SnapshotService Service() =>
        new(NullLogger<SnapshotService>.Instance, null!, null!, null!, null!, Path.GetTempPath());

    private static SystemStateSnapshot Snapshot(
        string label,
        IReadOnlyList<string>? tweaks = null,
        IReadOnlyList<string>? services = null,
        int minutesLater = 0) =>
        new()
        {
            Id = label,
            Label = label,
            CapturedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(minutesLater),
            AppliedTweakIds = tweaks ?? [],
            RunningServices = services ?? [],
            RamUsedMb = 8000,
            RamTotalMb = 32000,
            StandbyMb = 2000,
            ProcessCount = 200,
        };
}
