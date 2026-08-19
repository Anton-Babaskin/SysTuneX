using System.Diagnostics;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

public interface IProcessService
{
    OperationResult SetPriority(int processId, ProcessPriorityClass priority);

    OperationResult SetAffinity(int processId, nint affinityMask);

    /// <summary>Trims every accessible process's working set and drops the standby list.</summary>
    Task<MemoryTrimResult> TrimMemoryAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<ProcessInfo> GetTopProcessesByMemory(int count = 10);
}

public sealed record ProcessInfo(int Id, string Name, long WorkingSetBytes, string? Description);

/// <param name="TrimmedProcesses">Processes whose working set was successfully trimmed.</param>
/// <param name="StandbyPurged">The standby list was dropped (requires an elevated token).</param>
/// <param name="FreedBytes">Difference in available physical memory, measured around the operation.</param>
public sealed record MemoryTrimResult(int TrimmedProcesses, bool StandbyPurged, long FreedBytes);
