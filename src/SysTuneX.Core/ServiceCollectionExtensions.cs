using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using SysTuneX.Core.Services;
using SysTuneX.Core.Services.Sensors;

namespace SysTuneX.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything in SysTuneX.Core. All of these are singletons: they hold caches
    /// (hardware info, the backup journal, CPU time deltas) that only make sense process-wide.
    /// </summary>
    public static IServiceCollection AddSysTuneXCore(this IServiceCollection services)
    {
        // The host registers its own switch before calling this so the settings page can flip
        // logging at runtime; TryAdd keeps a standalone consumer of Core working without one.
        services.TryAddSingleton<LogLevelSwitch>();

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IEnvironmentService, EnvironmentService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IProcessService, ProcessService>();
        services.AddSingleton<IServiceManager, ServiceManager>();
        services.AddSingleton<IPowerService, PowerService>();
        services.AddSingleton<IPrivacyService, PrivacyService>();
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<ICleanupService, CleanupService>();
        services.AddSingleton<IRestorePointService, RestorePointService>();
        services.AddSingleton<IScheduledTaskService, ScheduledTaskService>();

        // Handlers are resolved by TweakEngine through IEnumerable<ISpecialTweakHandler>.
        services.AddSingleton<ISpecialTweakHandler, CoreParkingTweakHandler>();
        services.AddSingleton<ISpecialTweakHandler, HypervisorLaunchTweakHandler>();
        services.AddSingleton<ISpecialTweakHandler, NagleTweakHandler>();
        services.AddSingleton<ISpecialTweakHandler, PcieAspmTweakHandler>();
        services.AddSingleton<ISpecialTweakHandler, TelemetryTaskTweakHandler>();

        // Restorers are resolved as a set; each claims the journal entries it is responsible for,
        // and the rollback reports anything nobody claimed rather than dropping it.
        services.AddSingleton<IChangeRestorer, TweakChangeRestorer>();
        services.AddSingleton<IChangeRestorer, ServiceChangeRestorer>();
        services.AddSingleton<IChangeRestorer, PowerSchemeChangeRestorer>();
        services.AddSingleton<IChangeRestorer, PowerSettingChangeRestorer>();
        services.AddSingleton<IChangeRestorer, DnsChangeRestorer>();
        services.AddSingleton<IChangeRestorer, HostsChangeRestorer>();
        services.AddSingleton<IChangeRestorer, BootChangeRestorer>();
        services.AddSingleton<IChangeRestorer, ScheduledTaskChangeRestorer>();
        services.AddSingleton<IChangeRestorer, RegistryChangeRestorer>();
        services.AddSingleton<IChangeRollbackService, ChangeRollbackService>();
        services.AddSingleton<IAppliedProfileStore, AppliedProfileStore>();

        services.AddSingleton<ITweakEngine, TweakEngine>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        // Registered in order; the first that answers wins. A machine has one vendor's card,
        // so this needs no detection - the probe for a driver that is not installed says so.
        services.AddSingleton<IGpuSensorProbe, NvidiaGpuProbe>();
        services.AddSingleton<IGpuSensorProbe, AmdGpuProbe>();
        services.AddSingleton<ISensorService, SensorService>();
        // Started on demand rather than here: an ETW session is a machine-wide resource, and
        // there is no reason to hold one open for someone who never opens the monitor.
        services.AddSingleton<IFrameRateProbe, EtwFrameRateProbe>();
        services.AddSingleton<IGameModeService, GameModeService>();
        services.AddSingleton<IGameWatcher, GameWatcher>();
        services.AddSingleton<GameModeAutomation>();
        services.AddSingleton<ISnapshotService, SnapshotService>();
        services.AddSingleton<GameModeScheduler>();

        return services;
    }
}
