using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Which power scheme is active, and switching between them.
///
/// Split from the settings below because they are different questions asked by different callers.
/// The settings page and game mode want a scheme; a tweak wants one index inside whichever scheme
/// happens to be active and does not care which that is.
/// </summary>
public interface IPowerSchemeService
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
}

/// <summary>
/// One setting inside the active power scheme.
///
/// Core parking is here rather than in its own interface because that is what it is: two indexes
/// in the processor subgroup. Giving it a bespoke pair of methods made it look like a different
/// kind of thing and is why ASPM, which is the same shape, needed the generic pair added later.
/// </summary>
public interface IPowerSettingService
{
    /// <summary>
    /// Writes one setting on the active scheme, on mains and on battery, and re-activates the
    /// scheme so it takes effect. <paramref name="failureCode"/> is the message to report if
    /// powercfg refuses.
    /// </summary>
    Task<OperationResult> SetSchemeSettingAsync(
        string subgroup,
        string setting,
        int value,
        MessageTemplate failureCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One setting's current mains value, or null when powercfg will not answer - which is what a
    /// setting hidden on this machine looks like, and is not the same as a value of zero.
    /// </summary>
    Task<int?> GetSchemeSettingAsync(
        string subgroup,
        string setting,
        CancellationToken cancellationToken = default);

    /// <summary>Unparks CPU cores on the active scheme via the documented processor power settings.</summary>
    Task<OperationResult> SetCoreParkingAsync(bool enabled, CancellationToken cancellationToken = default);

    Task<bool> IsCoreParkingDisabledAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Both halves, for the one implementation and for the container.
///
/// SetHibernationAsync used to be here too and had no caller anywhere in the application - it was
/// implemented, translated and maintained for nobody. It is in the history if a page ever wants it.
/// </summary>
public interface IPowerService : IPowerSchemeService, IPowerSettingService;

public sealed record PowerScheme(Guid Guid, string Name, bool IsActive)
{
    /// <summary>Built-in Ultimate Performance scheme, present on Windows 10 1803+ workstation SKUs.</summary>
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
}
