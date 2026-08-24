using System.Diagnostics;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tests.Fakes;

/// <summary>An elevated machine by default, because the interesting paths need a full token.</summary>
public sealed class FakeEnvironment : IEnvironmentService
{
    public bool IsElevated { get; set; } = true;

    public WindowsVersionInfo Windows { get; set; } = new()
    {
        Major = 10,
        Minor = 0,
        Build = 26100,
        ProductName = "Windows 11 Pro",
        DisplayVersion = "24H2",
    };

    public string DataDirectory { get; set; } = Path.GetTempPath();

    public OperationResult RestartElevated() => OperationResult.Ok();

    public Task<OperationResult> RestartExplorerAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok());
}

/// <summary>
/// Records what was asked of it. Game mode's whole promise is that it starts back exactly what
/// it stopped, so the test needs to see the calls, not just the outcome.
/// </summary>
public sealed class FakeServiceManager : IServiceManager
{
    private readonly Dictionary<string, ServiceSnapshot> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(ServiceDefinition Definition, ServiceSnapshot State)> _managed = [];

    public List<string> Started { get; } = [];

    public List<string> Stopped { get; } = [];

    /// <summary>Services that refuse to stop, to prove a refusal is reported rather than swallowed.</summary>
    public HashSet<string> RefusesToStop { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Services that refuse to start again, which is the failure that would strand a machine.</summary>
    public HashSet<string> RefusesToStart { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FakeServiceManager Add(string name, RiskLevel risk, bool running)
    {
        var definition = new ServiceDefinition
        {
            ServiceName = name,
            DisplayName = name,
            Description = name,
            Risk = risk,
        };

        var state = new ServiceSnapshot
        {
            ServiceName = name,
            State = running ? ServiceState.Running : ServiceState.Stopped,
            StartMode = ServiceStartMode.Automatic,
        };

        _states[name] = state;
        _managed.Add((definition, state));
        return this;
    }

    public Task<IReadOnlyList<(ServiceDefinition Definition, ServiceSnapshot State)>> GetManagedServicesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<(ServiceDefinition, ServiceSnapshot)>>(
            [.. _managed.Select(m => (m.Definition, _states[m.Definition.ServiceName]))]);

    public ServiceSnapshot GetState(string serviceName) =>
        _states.TryGetValue(serviceName, out ServiceSnapshot? state)
            ? state
            : new ServiceSnapshot { ServiceName = serviceName, State = ServiceState.NotInstalled };

    public Task<OperationResult> DisableAsync(ServiceDefinition definition, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> RestoreAsync(string serviceName, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> StartAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (RefusesToStart.Contains(serviceName))
        {
            return Task.FromResult(OperationResult.Fail(CoreMessages.ServiceStartFailed, serviceName, "access denied"));
        }

        Started.Add(serviceName);
        SetRunning(serviceName, running: true);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> StopAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        if (RefusesToStop.Contains(serviceName))
        {
            return Task.FromResult(OperationResult.Fail(CoreMessages.ServiceCannotBeStopped, serviceName));
        }

        Stopped.Add(serviceName);
        SetRunning(serviceName, running: false);
        return Task.FromResult(OperationResult.Ok());
    }

    public OperationResult SetStartMode(string serviceName, ServiceStartMode startMode) => OperationResult.Ok();

    private void SetRunning(string serviceName, bool running)
    {
        if (_states.TryGetValue(serviceName, out ServiceSnapshot? state))
        {
            _states[serviceName] = state with { State = running ? ServiceState.Running : ServiceState.Stopped };
        }
    }
}

public sealed class FakePowerService : IPowerService
{
    public static readonly PowerScheme BalancedScheme = new(PowerScheme.Balanced, "Balanced", true);

    public Guid ActiveScheme { get; set; } = PowerScheme.Balanced;

    public List<Guid> Activated { get; } = [];

    /// <summary>Some editions have neither Ultimate nor High Performance.</summary>
    public bool HighPerformanceAvailable { get; set; } = true;

    public Task<IReadOnlyList<PowerScheme>> GetSchemesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PowerScheme>>([BalancedScheme]);

    public Task<PowerScheme?> GetActiveSchemeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<PowerScheme?>(new PowerScheme(ActiveScheme, ActiveScheme == PowerScheme.Balanced ? "Balanced" : "Other", true));

    public Task<OperationResult> ActivateHighPerformanceAsync(CancellationToken cancellationToken = default)
    {
        if (!HighPerformanceAvailable)
        {
            return Task.FromResult(OperationResult.Fail(CoreMessages.PowerNoHighPerformanceScheme));
        }

        ActiveScheme = PowerScheme.UltimatePerformance;
        Activated.Add(ActiveScheme);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> RestorePreviousSchemeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok());

    public Task<OperationResult> SetActiveSchemeAsync(Guid schemeGuid, CancellationToken cancellationToken = default)
    {
        ActiveScheme = schemeGuid;
        Activated.Add(schemeGuid);
        return Task.FromResult(OperationResult.Ok());
    }

    public Task<OperationResult> SetCoreParkingAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok());

    public Task<bool> IsCoreParkingDisabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<OperationResult> SetHibernationAsync(bool enabled, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok());
}

public sealed class FakeProcessService : IProcessService
{
    public int TrimCount { get; private set; }

    public long FreedBytes { get; set; } = 512L * 1024 * 1024;

    public OperationResult SetPriority(int processId, ProcessPriorityClass priority) => OperationResult.Ok();

    public OperationResult SetAffinity(int processId, nint affinityMask) => OperationResult.Ok();

    public Task<MemoryTrimResult> TrimMemoryAsync(CancellationToken cancellationToken = default)
    {
        TrimCount++;
        return Task.FromResult(new MemoryTrimResult(12, StandbyPurged: true, FreedBytes));
    }

    public IReadOnlyList<ProcessInfo> GetTopProcessesByMemory(int count = 10) => [];
}

/// <summary>A watcher whose detection the test drives directly.</summary>
public sealed class FakeGameWatcher : IGameWatcher
{
    public bool IsWatching { get; private set; }

    public WatchedGame? DetectedGame { get; private set; }

    public IReadOnlyList<WatchedGame> Games { get; } = [];

    public event EventHandler? DetectionChanged;

    /// <summary>Pretends a game started, or that the last one exited when given null.</summary>
    public void Detect(WatchedGame? game)
    {
        DetectedGame = game;
        DetectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<OperationResult> AddAsync(string processName, string displayName, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Ok());

    public Task RemoveAsync(string processName, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SetEnabledAsync(string processName, bool enabled, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void Start() => IsWatching = true;

    public void Stop() => IsWatching = false;

    public void Dispose() => IsWatching = false;
}
