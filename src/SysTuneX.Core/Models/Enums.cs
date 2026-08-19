namespace SysTuneX.Core.Models;

/// <summary>How much damage a change can do if it turns out to be wrong for this machine.</summary>
public enum RiskLevel
{
    /// <summary>Cosmetic or telemetry-level. Reverting restores the previous behaviour immediately.</summary>
    Safe,

    /// <summary>Changes real system behaviour. Some apps may notice; a sign-out or restart may be needed.</summary>
    Moderate,

    /// <summary>Touches security, the boot configuration or kernel scheduling. Requires an explicit confirmation.</summary>
    Advanced,
}

public enum TweakCategory
{
    Gaming,
    Windows11,
    Privacy,
    Network,
}

public enum TweakStatus
{
    /// <summary>The current value could not be read (missing key, no permission, unsupported build).</summary>
    Unknown,

    /// <summary>Every change belonging to the tweak is at its optimised value.</summary>
    Applied,

    /// <summary>None of the changes are at their optimised value.</summary>
    NotApplied,

    /// <summary>Some but not all of the changes are applied — the tweak is half-written.</summary>
    Partial,

    /// <summary>The tweak does not apply to the Windows build the app is running on.</summary>
    Unsupported,
}

/// <summary>Mirrors the Win32 service start type values used by <c>ChangeServiceConfig</c>.</summary>
public enum ServiceStartMode
{
    Unknown = -1,
    Boot = 0,
    System = 1,
    Automatic = 2,
    Manual = 3,
    Disabled = 4,
}

public enum ServiceState
{
    Unknown = 0,
    Stopped = 1,
    StartPending = 2,
    StopPending = 3,
    Running = 4,
    NotInstalled = 99,
}

/// <summary>What a single recorded backup entry knows how to put back.</summary>
public enum BackupKind
{
    RegistryValue,
    ServiceConfiguration,
    PowerScheme,
    HostsFile,
    DnsConfiguration,
    BootConfiguration,
}
