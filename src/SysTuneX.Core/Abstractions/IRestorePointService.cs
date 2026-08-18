using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

public interface IRestorePointService
{
    /// <summary>System Protection is switched on for the system drive, so a restore point can be created.</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a Windows restore point. Windows rate-limits these to one per 24 hours by
    /// default, so a "skipped" result is normal and is reported rather than treated as failure.
    /// </summary>
    Task<OperationResult> CreateAsync(string description, CancellationToken cancellationToken = default);
}
