using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <summary>
/// Turns game mode on and off by the clock.
///
/// Written as "is now inside the window?" checked once a minute, rather than as timers set for
/// the two edges. Timers miss their moment whenever the machine sleeps through it, and a missed
/// edge would leave game mode stuck on for the rest of the day; re-deciding from the current
/// time cannot get stuck.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GameModeScheduler : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger<GameModeScheduler> _logger;
    private readonly IGameModeService _gameMode;

    private CancellationTokenSource? _running;
    private GameModeSchedule _schedule = new();
    private bool _disposed;

    /// <summary>Replaced in tests so the window can be evaluated at a chosen moment.</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    public GameModeScheduler(ILogger<GameModeScheduler> logger, IGameModeService gameMode)
    {
        _logger = logger;
        _gameMode = gameMode;
    }

    public GameModeSchedule Schedule
    {
        get => _schedule;
        set
        {
            _schedule = value ?? new GameModeSchedule();

            if (_schedule.Enabled)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }
    }

    public bool IsRunning => _running is not null;

    public void Start()
    {
        if (_disposed || _running is not null)
        {
            return;
        }

        _running = new CancellationTokenSource();
        _ = LoopAsync(_running.Token);

        _logger.LogInformation(
            "Game mode scheduled from {Start} to {End}",
            _schedule.StartsAt,
            _schedule.EndsAt);
    }

    public void Stop()
    {
        CancellationTokenSource? running = Interlocked.Exchange(ref _running, null);
        running?.Cancel();
        running?.Dispose();
    }

    /// <summary>One decision. Exposed so tests can drive it without waiting a minute.</summary>
    internal async Task TickAsync(CancellationToken cancellationToken = default)
    {
        bool shouldBeOn = _schedule.Covers(Now());

        if (shouldBeOn == _gameMode.IsActive)
        {
            return;
        }

        if (shouldBeOn)
        {
            GameModeResult result = await _gameMode.EnableAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            Report(result, "on");
            return;
        }

        // Only a scheduled session is closed by the schedule. Someone who switched game mode on
        // by hand at 23:05 did not ask for it to end at 23:00.
        if (_gameMode.Session is { AutoStarted: false })
        {
            return;
        }

        GameModeResult off = await _gameMode.DisableAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        Report(off, "off");
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            await TickAsync(cancellationToken).ConfigureAwait(false);

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await TickAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() was called.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The game mode scheduler stopped unexpectedly");
        }
    }

    private void Report(GameModeResult result, string direction)
    {
        if (result.Result.Success)
        {
            _logger.LogInformation("Schedule turned game mode {Direction}", direction);
            return;
        }

        _logger.LogWarning(
            "Schedule could not turn game mode {Direction}: {Message}",
            direction,
            result.Result.Message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
