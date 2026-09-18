using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Frees memory by trimming working sets and dropping the standby list.
///
/// One method, because one method is what anything ever asked for. This was IMemoryTrimmer with
/// four: SetPriority, SetAffinity and GetTopProcessesByMemory had no caller anywhere in the
/// application and were implemented, translated and maintained regardless. They are in the history
/// if a page ever wants them; carrying them as a promise nothing kept was the worse option.
/// </summary>
public interface IMemoryTrimmer
{
    /// <summary>
    /// Trims every accessible process's working set and drops the standby list.
    ///
    /// Worth being honest about what this is: pages the trimmed processes still want come straight
    /// back from disk, so it buys free memory now at the cost of some paging afterwards. It earns
    /// its place before a game starts and nowhere else.
    /// </summary>
    Task<MemoryTrimResult> TrimMemoryAsync(CancellationToken cancellationToken = default);
}

/// <param name="TrimmedProcesses">Processes whose working set was successfully trimmed.</param>
/// <param name="StandbyPurged">The standby list was dropped (requires an elevated token).</param>
/// <param name="FreedBytes">Difference in available physical memory, measured around the operation.</param>
public sealed record MemoryTrimResult(int TrimmedProcesses, bool StandbyPurged, long FreedBytes);
