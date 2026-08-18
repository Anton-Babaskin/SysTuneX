namespace SysTuneX.Core.Models;

/// <summary>A Windows service SysTuneX is willing to touch, with the reason it is on the list.</summary>
public sealed record ServiceDefinition
{
    public required string ServiceName { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public required RiskLevel Risk { get; init; }

    /// <summary>Start type to set when the service is turned off. Manual is safer than Disabled for shared services.</summary>
    public ServiceStartMode DisabledStartMode { get; init; } = ServiceStartMode.Disabled;

    /// <summary>Build this service first appeared in (Windows 11 services do not exist on Windows 10).</summary>
    public int MinBuild { get; init; }

    /// <summary>Per-user service instances get a random "_1a2b3" suffix; match on the stem instead.</summary>
    public bool IsPerUserService { get; init; }
}

/// <summary>Live state of a managed service.</summary>
public sealed record ServiceSnapshot
{
    public required string ServiceName { get; init; }
    public ServiceState State { get; init; } = ServiceState.Unknown;
    public ServiceStartMode StartMode { get; init; } = ServiceStartMode.Unknown;

    /// <summary>The service is not present on this machine (edition or build difference).</summary>
    public bool IsInstalled => State != ServiceState.NotInstalled;

    public bool IsRunning => State is ServiceState.Running or ServiceState.StartPending;

    /// <summary>Stopped and set to never start again — what SysTuneX considers "optimised".</summary>
    public bool IsDisabled => StartMode == ServiceStartMode.Disabled;
}
