using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IGameModeService"/>
[SupportedOSPlatform("windows")]
public sealed class GameModeService : IGameModeService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ILogger<GameModeService> _logger;
    private readonly IEnvironmentService _environment;
    private readonly IServiceManager _services;
    private readonly IPowerService _power;
    private readonly IProcessService _processes;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly string _sessionFile = Path.Combine(AppPaths.DataDirectory, "gamemode.json");

    public GameModeService(
        ILogger<GameModeService> logger,
        IEnvironmentService environment,
        IServiceManager services,
        IPowerService power,
        IProcessService processes)
    {
        _logger = logger;
        _environment = environment;
        _services = services;
        _power = power;
        _processes = processes;
    }

    public bool IsActive => Session is not null;

    public GameModeSession? Session { get; private set; }

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_sessionFile))
            {
                return;
            }

            await using FileStream stream = File.OpenRead(_sessionFile);
            Session = await JsonSerializer
                .DeserializeAsync<GameModeSession>(stream, Json, cancellationToken)
                .ConfigureAwait(false);

            if (Session is not null)
            {
                _logger.LogInformation(
                    "Game mode was still on from {StartedAt}: {Count} services to start again",
                    Session.StartedAt,
                    Session.StoppedServices.Count);
            }
        }
        catch (Exception ex)
        {
            // A session file we cannot read must not block the app, but it must not be silently
            // treated as "game mode is off" either - that would strand stopped services.
            _logger.LogError(ex, "The game mode session file could not be read: {Path}", _sessionFile);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<GameModeResult> EnableAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_environment.IsElevated)
        {
            return GameModeResult.Failed("Game mode needs administrator rights to stop services and switch the power scheme.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsActive)
            {
                return new GameModeResult(OperationResult.NoChange("Game mode is already on."), 0, 0, []);
            }

            var notes = new List<string>();

            progress?.Report("power");
            (Guid? previousScheme, string previousName) = await SwitchPowerSchemeAsync(notes, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report("services");
            IReadOnlyList<string> stopped = await StopSafeServicesAsync(notes, cancellationToken).ConfigureAwait(false);

            progress?.Report("memory");
            long freedMb = await TrimAsync(notes, cancellationToken).ConfigureAwait(false);

            Session = new GameModeSession
            {
                StartedAt = DateTimeOffset.Now,
                StoppedServices = stopped,
                PreviousPowerScheme = previousScheme,
                PreviousPowerSchemeName = previousName,
                FreedMemoryMb = freedMb,
            };

            await SaveSessionAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Game mode on: {Services} services stopped, {Freed} MB freed, previous scheme {Scheme}",
                stopped.Count,
                freedMb,
                previousName);

            Changed?.Invoke(this, EventArgs.Empty);
            return new GameModeResult(OperationResult.Ok(), stopped.Count, freedMb, notes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game mode could not be turned on");
            return GameModeResult.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GameModeResult> DisableAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Session is not { } session)
            {
                return new GameModeResult(OperationResult.NoChange("Game mode is not on."), 0, 0, []);
            }

            var notes = new List<string>();
            int restarted = 0;

            progress?.Report("services");
            foreach (string serviceName in session.StoppedServices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                OperationResult result = await _services.StartAsync(serviceName, cancellationToken).ConfigureAwait(false);
                if (result.Success)
                {
                    restarted++;
                }
                else
                {
                    notes.Add($"{serviceName}: {result.Message}");
                    _logger.LogWarning("Could not start {Service} again: {Message}", serviceName, result.Message);
                }
            }

            progress?.Report("power");
            if (session.PreviousPowerScheme is { } scheme)
            {
                OperationResult result = await _power.SetActiveSchemeAsync(scheme, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    notes.Add($"Power scheme: {result.Message}");
                }
            }

            Session = null;
            DeleteSession();

            _logger.LogInformation("Game mode off: {Restarted} services started again", restarted);

            Changed?.Invoke(this, EventArgs.Empty);
            return new GameModeResult(OperationResult.Ok(), restarted, 0, notes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game mode could not be turned off");
            return GameModeResult.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<(Guid? Guid, string Name)> SwitchPowerSchemeAsync(
        List<string> notes,
        CancellationToken cancellationToken)
    {
        try
        {
            PowerScheme? active = await _power.GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);

            OperationResult result = await _power.ActivateHighPerformanceAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                notes.Add($"Power scheme: {result.Message}");
                return (null, string.Empty);
            }

            // Only worth restoring if we know what it was; otherwise leave the field null so
            // turning game mode off does not switch the machine to a scheme it never had.
            return active is null ? (null, string.Empty) : (active.Guid, active.Name);
        }
        catch (Exception ex)
        {
            notes.Add($"Power scheme: {ex.Message}");
            return (null, string.Empty);
        }
    }

    /// <summary>
    /// Stops the services the catalog grades as safe, and only those actually running.
    /// Stopping is not disabling: the start type is untouched, so a service Windows decides it
    /// needs can still start, and the next boot is exactly as it was.
    /// </summary>
    private async Task<IReadOnlyList<string>> StopSafeServicesAsync(
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var stopped = new List<string>();

        IReadOnlyList<(ServiceDefinition Definition, ServiceSnapshot State)> managed =
            await _services.GetManagedServicesAsync(cancellationToken).ConfigureAwait(false);

        foreach ((ServiceDefinition definition, ServiceSnapshot state) in managed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (definition.Risk != RiskLevel.Safe || !state.IsRunning)
            {
                continue;
            }

            OperationResult result = await _services.StopAsync(definition.ServiceName, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                stopped.Add(definition.ServiceName);
            }
            else
            {
                notes.Add($"{definition.DisplayName}: {result.Message}");
            }
        }

        return stopped;
    }

    private async Task<long> TrimAsync(List<string> notes, CancellationToken cancellationToken)
    {
        try
        {
            MemoryTrimResult trim = await _processes.TrimMemoryAsync(cancellationToken).ConfigureAwait(false);
            if (!trim.StandbyPurged)
            {
                notes.Add("The standby list was not dropped; working sets were still trimmed.");
            }

            return Math.Max(0, trim.FreedBytes / (1024 * 1024));
        }
        catch (Exception ex)
        {
            notes.Add($"Memory trim: {ex.Message}");
            return 0;
        }
    }

    private async Task SaveSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            await using FileStream stream = File.Create(_sessionFile);
            await JsonSerializer.SerializeAsync(stream, Session, Json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The session is still live in memory, so this is a warning rather than a failure -
            // but it does mean an app restart would forget what to put back.
            _logger.LogWarning(ex, "The game mode session could not be saved to {Path}", _sessionFile);
        }
    }

    private void DeleteSession()
    {
        try
        {
            if (File.Exists(_sessionFile))
            {
                File.Delete(_sessionFile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The game mode session file could not be deleted");
        }
    }
}
