using Microsoft.Win32;

namespace SysTuneX.Core.Models;

/// <summary>
/// A single registry value a tweak owns.
/// </summary>
/// <param name="KeyPath">Full path including the hive, e.g. <c>HKLM\SOFTWARE\...</c>.</param>
/// <param name="ValueName">Value name inside the key.</param>
/// <param name="OptimizedValue">The value written when the tweak is enabled.</param>
/// <param name="WindowsDefaultValue">
/// The documented stock-Windows value. <see langword="null"/> means the value does not exist on a
/// clean install, in which case reverting deletes it rather than writing a guess. This is only a
/// fallback: if SysTuneX recorded the machine's own previous value, that wins.
/// </param>
/// <param name="ValueKind">Registry type to write.</param>
public sealed record RegistryChange(
    string KeyPath,
    string ValueName,
    object OptimizedValue,
    object? WindowsDefaultValue,
    RegistryValueKind ValueKind);

/// <summary>Work that has to happen after the registry write for the change to take effect live.</summary>
[Flags]
public enum PostApplyAction
{
    None = 0,

    /// <summary>Broadcast WM_SETTINGCHANGE so already-running apps pick the value up.</summary>
    BroadcastSettingChange = 1 << 0,

    /// <summary>Push the mouse speed/threshold triple through SystemParametersInfo(SPI_SETMOUSE).</summary>
    RefreshMouseSettings = 1 << 1,

    /// <summary>Re-apply the desktop animation/UI-effects flags through SystemParametersInfo.</summary>
    RefreshVisualEffects = 1 << 2,
}

/// <summary>
/// One optimisation. A tweak owns one or more registry values and knows both how to turn
/// itself on and — critically — what the machine looked like before it did.
/// </summary>
public sealed record TweakDefinition
{
    public required string Id { get; init; }

    public required TweakCategory Category { get; init; }

    /// <summary>Resource key of the group header this tweak is listed under.</summary>
    public required string GroupKey { get; init; }

    /// <summary>Neutral (English) display name. The UI prefers the localized resource <c>Tweak_{Id}_Name</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Neutral (English) description. The UI prefers the localized resource <c>Tweak_{Id}_Desc</c>.</summary>
    public required string Description { get; init; }

    public required RiskLevel Risk { get; init; }

    public IReadOnlyList<RegistryChange> Changes { get; init; } = [];

    /// <summary>
    /// Set for tweaks that are not a plain registry write (core parking, hypervisor launch type).
    /// The engine routes these to a registered <see cref="Abstractions.ISpecialTweakHandler"/>.
    /// </summary>
    public string? HandlerKey { get; init; }

    public PostApplyAction PostApply { get; init; } = PostApplyAction.None;

    /// <summary>The change only takes full effect after a reboot.</summary>
    public bool RequiresRestart { get; init; }

    /// <summary>The change only takes full effect after signing out and back in.</summary>
    public bool RequiresSignOut { get; init; }

    /// <summary>Explorer has to be restarted before the change is visible (taskbar items, shell flags).</summary>
    public bool RequiresExplorerRestart { get; init; }

    /// <summary>Minimum Windows build this tweak applies to. 0 means "any supported build".</summary>
    public int MinBuild { get; init; }

    /// <summary>Highest Windows build this tweak applies to (inclusive).</summary>
    public int MaxBuild { get; init; } = int.MaxValue;

    /// <summary>Show a blocking confirmation before applying. Always true for <see cref="RiskLevel.Advanced"/>.</summary>
    public bool RequiresConfirmation => Risk == RiskLevel.Advanced;

    /// <summary>Extra caution shown in the confirmation dialog (anti-cheat, BitLocker, …).</summary>
    public string? WarningKey { get; init; }

    public bool AppliesTo(WindowsVersionInfo windows) =>
        windows.Build >= MinBuild && windows.Build <= MaxBuild;
}
