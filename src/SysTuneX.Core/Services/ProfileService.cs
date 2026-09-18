using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IProfileService"/>
[SupportedOSPlatform("windows")]
public sealed class ProfileService : IProfileService
{
    private readonly ILogger<ProfileService> _logger;
    private readonly ITweakEngine _tweaks;
    private readonly IServiceManager _services;
    private readonly IPowerService _power;
    private readonly IProcessService _processes;
    private readonly IBackupService _backup;
    private readonly IRestorePointService _restorePoints;
    private readonly IPrivacyService _privacy;
    private readonly INetworkService _network;
    private readonly IEnvironmentService _environment;
    private readonly IRegistryService _registry;

    private readonly IAppliedProfileStore _applied;
    private readonly IChangeRollbackService _rollback;

    public ProfileService(
        ILogger<ProfileService> logger,
        ITweakEngine tweaks,
        IServiceManager services,
        IPowerService power,
        IProcessService processes,
        IBackupService backup,
        IRestorePointService restorePoints,
        IPrivacyService privacy,
        INetworkService network,
        IEnvironmentService environment,
        IRegistryService registry,
        IAppliedProfileStore applied,
        IChangeRollbackService rollback)
    {
        _logger = logger;
        _tweaks = tweaks;
        _services = services;
        _power = power;
        _processes = processes;
        _backup = backup;
        _restorePoints = restorePoints;
        _privacy = privacy;
        _network = network;
        _environment = environment;
        _registry = registry;
        _applied = applied;
        _rollback = rollback;
    }

    /// <inheritdoc />
    public AppliedProfile? Applied => _applied.Current;

    public IReadOnlyList<GameProfile> GetProfiles() => GameProfiles.BuiltIn;

    public IReadOnlyList<TweakDefinition> ResolveTweaks(GameProfile profile, bool includeAdvanced)
    {
        RiskLevel ceiling = includeAdvanced ? profile.MaxRisk : (RiskLevel)Math.Min((int)profile.MaxRisk, (int)RiskLevel.Moderate);

        return profile.TweakIds
            .Select(_tweaks.Find)
            .Where(t => t is not null)
            .Select(t => t!)
            .Where(t => t.Risk <= ceiling)
            // Gate on the build number rather than reading live status: resolving a handler
            // tweak's status shells out to powercfg, and this runs while building the UI.
            .Where(t => t.AppliesTo(_environment.Windows))
            .ToList();
    }

    public Task<double> GetCompletionAsync(GameProfile profile, CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                List<TweakDefinition> tweaks = ResolveTweaks(profile, includeAdvanced: true).ToList();
                if (tweaks.Count == 0)
                {
                    return 0d;
                }

                int applied = tweaks.Count(t => _tweaks.GetStatus(t) == TweakStatus.Applied);
                return (double)applied / tweaks.Count;
            },
            cancellationToken);
    }

    /// <summary>
    /// Reading a value per registry change and a state per service is blocking work. Running it
    /// here rather than making every caller remember to is the difference between an interface
    /// that is easy to use correctly and one that freezes a window when someone forgets.
    /// </summary>
    public Task<ProfilePreview> PreviewAsync(
        GameProfile profile,
        bool includeAdvanced,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return Task.Run(() => BuildPreview(profile, includeAdvanced, cancellationToken), cancellationToken);
    }

    private ProfilePreview BuildPreview(
        GameProfile profile,
        bool includeAdvanced,
        CancellationToken cancellationToken)
    {

        IReadOnlyList<TweakDefinition> tweaks = ResolveTweaks(profile, includeAdvanced);
        var previews = new List<TweakPreview>(tweaks.Count);

        foreach (TweakDefinition tweak in tweaks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool applied = _tweaks.GetStatus(tweak) == TweakStatus.Applied;

            var values = new List<ValueChangePreview>(tweak.Changes.Count);
            foreach (RegistryChange change in tweak.Changes)
            {
                object? current = _registry.GetValue(change.KeyPath, change.ValueName);

                values.Add(new ValueChangePreview(
                    change.KeyPath,
                    change.ValueName,
                    current is null ? null : RegistryValueComparer.Stringify(current),
                    RegistryValueComparer.Stringify(change.OptimizedValue),
                    change.WindowsDefaultValue is null
                        ? null
                        : RegistryValueComparer.Stringify(change.WindowsDefaultValue)));
            }

            previews.Add(new TweakPreview(
                tweak.Id,
                tweak.Name,
                tweak.Risk,
                applied,
                tweak.RequiresRestart,
                tweak.RequiresSignOut,
                values));
        }

        var services = new List<ServicePreview>();
        foreach (string serviceName in profile.ServiceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ServiceCatalog.Find(serviceName) is not { } definition)
            {
                continue;
            }

            ServiceSnapshot state = _services.GetState(serviceName);
            if (!state.IsInstalled)
            {
                // A service Windows does not have on this edition is not a change anyone can
                // act on, so it is left out rather than listed as "would do nothing".
                continue;
            }

            services.Add(new ServicePreview(
                serviceName,
                definition.DisplayName,
                definition.Risk,
                state.StartMode,
                definition.DisabledStartMode,
                state.StartMode != definition.DisabledStartMode));
        }

        return new ProfilePreview
        {
            ProfileId = profile.Id,
            Tweaks = previews,
            Services = services,
            ChangesPowerScheme = profile.ActivateHighPerformancePower,
            TrimsMemory = profile.TrimMemory,
        };
    }

    public async Task<ProfileApplyResult> ApplyAsync(
        GameProfile profile,
        ProfileApplyOptions options,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        bool restorePointCreated = false;
        string? restorePointMessage = null;

        if (options.CreateRestorePoint)
        {
            progress?.Report(new BatchProgress("System restore point", 0, 1));

            OperationResult restore = await _restorePoints
                .CreateAsync($"SysTuneX - before applying {profile.Name}", cancellationToken)
                .ConfigureAwait(false);

            restorePointCreated = restore.Success;
            restorePointMessage = restore.Message;

            if (!restore.Success)
            {
                _logger.LogWarning("Restore point was not created: {Message}", restore.Message);
            }
        }

        IReadOnlyList<TweakDefinition> tweaks = ResolveTweaks(profile, options.IncludeAdvanced);
        BatchResult tweakResult = await _tweaks.ApplyManyAsync(tweaks, progress, cancellationToken).ConfigureAwait(false);
        errors.AddRange(tweakResult.Errors);

        int servicesChanged = 0;
        int servicesFailed = 0;

        for (int i = 0; i < profile.ServiceNames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string serviceName = profile.ServiceNames[i];
            ServiceDefinition? definition = ServiceCatalog.Find(serviceName);
            if (definition is null)
            {
                continue;
            }

            // A profile must never silently cross the risk line the user agreed to.
            if (!options.IncludeAdvanced && definition.Risk == RiskLevel.Advanced)
            {
                continue;
            }

            progress?.Report(new BatchProgress(definition.DisplayName, i, profile.ServiceNames.Count));

            OperationResult result = await _services.DisableAsync(definition, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                servicesChanged++;
            }
            else
            {
                servicesFailed++;
                errors.Add($"{definition.DisplayName}: {result.Message}");
            }
        }

        bool powerChanged = false;
        if (profile.ActivateHighPerformancePower)
        {
            progress?.Report(new BatchProgress("Power scheme", 0, 1));

            OperationResult power = await _power.ActivateHighPerformanceAsync(cancellationToken).ConfigureAwait(false);
            powerChanged = power.Success && power.Changed;

            if (!power.Success)
            {
                errors.Add(power.Message ?? "The power scheme could not be changed.");
            }
        }

        long freed = 0;
        if (profile.TrimMemory)
        {
            progress?.Report(new BatchProgress("Memory", 0, 1));
            MemoryTrimResult trim = await _processes.TrimMemoryAsync(cancellationToken).ConfigureAwait(false);
            freed = trim.FreedBytes;
        }

        _logger.LogInformation(
            "Profile {Profile}: {Applied} tweaks applied, {Failed} failed, {Services} services changed",
            profile.Id,
            tweakResult.Succeeded,
            tweakResult.Failed,
            servicesChanged);

        // Recorded after the work, so a run that threw before touching anything does not leave the
        // page claiming a profile the machine never received.
        _applied.Write(new AppliedProfile
        {
            ProfileId = profile.Id,
            AppliedAt = DateTimeOffset.Now,
            IncludedAdvanced = options.IncludeAdvanced,
        });

        return new ProfileApplyResult
        {
            Tweaks = tweakResult,
            ServicesChanged = servicesChanged,
            ServicesFailed = servicesFailed,
            PowerSchemeChanged = powerChanged,
            MemoryFreedBytes = freed,
            RestorePointCreated = restorePointCreated,
            RestorePointMessage = restorePointMessage,
            Errors = errors,
        };
    }

    /// <summary>
    /// Puts back everything SysTuneX recorded.
    ///
    /// This used to be a chain of <c>if (active.Any(e =&gt; e.Kind == X))</c> blocks right here, one
    /// per kind of change, which meant a new kind nobody remembered to add was recorded faithfully,
    /// shown in the journal, and then silently skipped by the button whose whole promise is that it
    /// puts everything back. The work is <see cref="IChangeRollbackService"/>'s now, and anything no
    /// restorer claims comes back as an error instead of vanishing.
    /// </summary>
    public async Task<ProfileApplyResult> RestoreEverythingAsync(
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RollbackReport report = await _rollback
            .RestoreEverythingAsync(progress, cancellationToken)
            .ConfigureAwait(false);

        var errors = new List<string>(report.AllErrors);

        foreach (BackupEntry orphan in report.Unclaimed)
        {
            errors.Add($"{orphan.Kind} \u2014 {orphan.Target}: nothing knows how to restore this.");
        }

        RestoreOutcome tweaks = report.For("tweaks");
        RestoreOutcome services = report.For("services");

        // Everything recorded has been put back, so no profile is applied any more. Leaving the
        // record would let the page keep naming a profile whose tweaks are gone.
        _applied.Write(null);

        return new ProfileApplyResult
        {
            Tweaks = new BatchResult(tweaks.Changed, tweaks.Failed, 0, tweaks.Errors),
            ServicesChanged = services.Changed,
            ServicesFailed = services.Failed,
            PowerSchemeChanged = report.For("power").Changed > 0,
            Errors = errors,
        };
    }
}
