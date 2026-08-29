namespace SysTuneX.Core.Models;

/// <summary>
/// Exactly what applying a profile would do, before it does it.
///
/// The project's rule is that no optimisation is a black box: each one names the registry value
/// it writes and the value Windows shipped. A profile applies a dozen of them at once, which is
/// where that rule is easiest to lose — so this is the rule kept at profile scale.
/// </summary>
public sealed record ProfilePreview
{
    public required string ProfileId { get; init; }

    public IReadOnlyList<TweakPreview> Tweaks { get; init; } = [];

    /// <summary>Services the profile would switch off, with what they are called on screen.</summary>
    public IReadOnlyList<ServicePreview> Services { get; init; } = [];

    /// <summary>The profile asks for a high performance scheme.</summary>
    public bool ChangesPowerScheme { get; init; }

    public bool TrimsMemory { get; init; }

    /// <summary>Tweaks that would actually change something; the rest are already in place.</summary>
    public int PendingCount => Tweaks.Count(t => !t.AlreadyApplied);

    public int PendingServiceCount => Services.Count(s => s.WouldChange);

    public bool RequiresRestart => Tweaks.Any(t => !t.AlreadyApplied && t.RequiresRestart);

    public bool RequiresSignOut => Tweaks.Any(t => !t.AlreadyApplied && t.RequiresSignOut);

    public bool HasAdvanced => Tweaks.Any(t => !t.AlreadyApplied && t.Risk == RiskLevel.Advanced);

    /// <summary>Nothing to do: everything in the profile is already in place.</summary>
    public bool IsNoOp => PendingCount == 0 && PendingServiceCount == 0;
}

/// <param name="AlreadyApplied">True when the machine is already in this state, so applying is a no-op.</param>
public sealed record TweakPreview(
    string TweakId,
    string Name,
    RiskLevel Risk,
    bool AlreadyApplied,
    bool RequiresRestart,
    bool RequiresSignOut,
    IReadOnlyList<ValueChangePreview> Values)
{
    /// <summary>
    /// A tweak with no registry values is handled by code rather than a plain write — core
    /// parking, the hypervisor boot flag. Saying so is better than showing an empty list.
    /// </summary>
    public bool IsHandlerDriven => Values.Count == 0;
}

/// <param name="Current">What the machine has now, or null when the value does not exist yet.</param>
/// <param name="Optimized">What the tweak would write.</param>
/// <param name="WindowsDefault">What Windows shipped, or null when it ships without the value.</param>
public sealed record ValueChangePreview(
    string KeyPath,
    string ValueName,
    string? Current,
    string Optimized,
    string? WindowsDefault)
{
    public bool WouldChange => !string.Equals(Current, Optimized, StringComparison.OrdinalIgnoreCase);
}

/// <param name="WouldChange">False when the service is already at the start mode the profile wants.</param>
public sealed record ServicePreview(
    string ServiceName,
    string DisplayName,
    RiskLevel Risk,
    ServiceStartMode CurrentStartMode,
    ServiceStartMode TargetStartMode,
    bool WouldChange);
