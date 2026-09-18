using System.Runtime.Versioning;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Native;

namespace SysTuneX.Core.Services;

/// <summary>
/// Pushes the mouse speed and threshold triple through SystemParametersInfo.
///
/// The registry value alone does nothing until the next sign-in: Windows reads the mouse curve
/// once and caches it, which is why a tweak that only wrote the key appeared to do nothing and
/// then worked the next morning.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MouseSettingsRefresh : IPostApplyEffect
{
    public PostApplyAction Flag => PostApplyAction.RefreshMouseSettings;

    public void Run(bool applying) => NativeHelpers.ApplyMouseSettings(accelerationEnabled: !applying);
}

/// <summary>Re-applies the desktop animation and UI-effects flags, for the same reason.</summary>
[SupportedOSPlatform("windows")]
public sealed class VisualEffectsRefresh : IPostApplyEffect
{
    public PostApplyAction Flag => PostApplyAction.RefreshVisualEffects;

    public void Run(bool applying) => NativeHelpers.ApplyUiEffects(enabled: !applying);
}

/// <summary>Broadcasts WM_SETTINGCHANGE so already-running applications re-read the value.</summary>
[SupportedOSPlatform("windows")]
public sealed class SettingChangeBroadcast : IPostApplyEffect
{
    public PostApplyAction Flag => PostApplyAction.BroadcastSettingChange;

    public void Run(bool applying) => NativeHelpers.BroadcastSettingChange();
}
