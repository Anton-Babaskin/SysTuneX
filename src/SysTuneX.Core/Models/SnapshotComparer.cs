namespace SysTuneX.Core.Models;

/// <summary>
/// What changed between two snapshots of the machine.
///
/// Pure arithmetic over two records, and it lives here rather than inside SnapshotService for the
/// reason that keeps coming up in this codebase: the service also captures state and owns a JSON
/// file, so reaching the comparison from a test meant constructing it with four <c>null!</c>
/// dependencies it would never use. The rules below are the part that can be wrong; capturing and
/// saving are not.
/// </summary>
public static class SnapshotComparer
{
    /// <summary>
    /// The smallest change worth reporting, and the proportion above it.
    ///
    /// Memory and process counts move on their own while nobody is doing anything. Listing a 3 MB
    /// difference as the effect of a tweak would be a lie dressed as data - which is the whole
    /// reason this comparison exists rather than a table of raw numbers.
    /// </summary>
    private const long MinimumDelta = 16;
    private const long ProportionDivisor = 20;

    public static SnapshotComparison Compare(SystemStateSnapshot before, SystemStateSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        // Ordered by time rather than by argument, so picking them the wrong way round in the
        // interface still reads as "this became that".
        if (after.CapturedAt < before.CapturedAt)
        {
            (before, after) = (after, before);
        }

        var changes = new List<SnapshotChange>();

        AddSetDifferences(changes, "Tweak", before.AppliedTweakIds, after.AppliedTweakIds, "applied", "not applied");
        AddSetDifferences(changes, "Service", before.RunningServices, after.RunningServices, "running", "stopped");

        if (before.PowerScheme != after.PowerScheme)
        {
            changes.Add(new SnapshotChange("Power", "scheme", before.PowerSchemeName, after.PowerSchemeName));
        }

        AddNumericChange(changes, "Memory", "used", before.RamUsedMb, after.RamUsedMb, "MB");
        AddNumericChange(changes, "Memory", "standby", before.StandbyMb, after.StandbyMb, "MB");
        AddNumericChange(changes, "System", "processes", before.ProcessCount, after.ProcessCount, string.Empty);

        return new SnapshotComparison(before, after, changes);
    }

    /// <summary>Reports what entered the set and what left it, rather than the counts.</summary>
    private static void AddSetDifferences(
        List<SnapshotChange> changes,
        string category,
        IReadOnlyList<string> before,
        IReadOnlyList<string> after,
        string presentLabel,
        string absentLabel)
    {
        var wasThere = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
        var isThere = new HashSet<string>(after, StringComparer.OrdinalIgnoreCase);

        foreach (string added in isThere.Except(wasThere, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            changes.Add(new SnapshotChange(category, added, absentLabel, presentLabel));
        }

        foreach (string removed in wasThere.Except(isThere, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            changes.Add(new SnapshotChange(category, removed, presentLabel, absentLabel));
        }
    }

    /// <summary>A number only counts as changed once it has moved further than it drifts on its own.</summary>
    private static void AddNumericChange(
        List<SnapshotChange> changes,
        string category,
        string item,
        long before,
        long after,
        string unit)
    {
        long delta = Math.Abs(after - before);
        long threshold = Math.Max(MinimumDelta, before / ProportionDivisor);

        if (delta < threshold)
        {
            return;
        }

        string suffix = string.IsNullOrEmpty(unit) ? string.Empty : " " + unit;
        changes.Add(new SnapshotChange(category, item, $"{before}{suffix}", $"{after}{suffix}"));
    }
}
