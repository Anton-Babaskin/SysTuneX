using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

public interface IPowerService
{
    /// <summary>Power schemes registered on this machine.</summary>
    Task<IReadOnlyList<PowerScheme>> GetSchemesAsync(CancellationToken cancellationToken = default);

    Task<PowerScheme?> GetActiveSchemeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches to Ultimate Performance, creating it first if the machine does not have it.
    /// <c>powercfg /duplicatescheme</c> mints a brand new GUID, so the new GUID is parsed from
    /// the command output instead of assuming the well-known one stays valid.
    /// </summary>
    Task<OperationResult> ActivateHighPerformanceAsync(CancellationToken cancellationToken = default);

    /// <summary>Restores the scheme that was active before SysTuneX changed it.</summary>
    Task<OperationResult> RestorePreviousSchemeAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SetActiveSchemeAsync(Guid schemeGuid, CancellationToken cancellationToken = default);

    /// <summary>Unparks CPU cores on the active scheme via the documented processor power settings.</summary>
    Task<OperationResult> SetCoreParkingAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<bool> IsCoreParkingDisabledAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> SetHibernationAsync(bool enabled, CancellationToken cancellationToken = default);
}

public sealed record PowerScheme(Guid Guid, string Name, bool IsActive)
{
    /// <summary>Built-in Ultimate Performance scheme, present on Windows 10 1803+ workstation SKUs.</summary>
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
}
