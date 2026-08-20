using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Records what the machine looks like, so two moments can be compared afterwards.
///
/// The point is to replace "it feels smoother" with a list of what actually changed.
/// </summary>
public interface ISnapshotService
{
    IReadOnlyList<SystemStateSnapshot> Snapshots { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the current state. Does real work - registry, services, powercfg - so keep it off the UI thread.</summary>
    Task<SystemStateSnapshot> CaptureAsync(string label, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists every difference between two snapshots, oldest first.</summary>
    SnapshotComparison Compare(SystemStateSnapshot before, SystemStateSnapshot after);
}
