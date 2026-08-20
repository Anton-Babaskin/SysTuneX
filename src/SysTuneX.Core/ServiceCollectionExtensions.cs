using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using SysTuneX.Core.Services;

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

        // Handlers are resolved by TweakEngine through IEnumerable<ISpecialTweakHandler>.
        services.AddSingleton<ISpecialTweakHandler, CoreParkingTweakHandler>();
        services.AddSingleton<ISpecialTweakHandler, HypervisorLaunchTweakHandler>();
        services.AddSingleton<ISpecialTweakHandler, NagleTweakHandler>();

        services.AddSingleton<ITweakEngine, TweakEngine>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<ISensorService, SensorService>();
        services.AddSingleton<IGameModeService, GameModeService>();

        return services;
    }
}
