using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

public interface ICleanupService
{
    /// <summary>Every cleanup target that resolves to at least one existing directory on this machine.</summary>
    IReadOnlyList<CleanupTarget> GetTargets();

    Task<CleanupScanResult> ScanAsync(CleanupTarget target, CancellationToken cancellationToken = default);

    Task<CleanupRunResult> CleanAsync(
        IEnumerable<CleanupTarget> targets,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppPackage>> GetRemovableAppsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveAppAsync(string packageFamilyName, CancellationToken cancellationToken = default);
}

public sealed record CleanupProgress(string TargetName, int CompletedTargets, int TotalTargets, long FreedBytes);
