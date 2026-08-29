using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <summary>
/// Joins the watcher to game mode: a watched game appears, game mode goes on; it exits, game
/// mode goes off again.
///
/// Kept out of both of them on purpose. The watcher should not know what to do about a game, and
/// game mode should work identically whether a person or a process started it — this is the only
/// piece that knows they are connected.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GameModeAutomation : IDisposable
{
    private readonly ILogger<GameModeAutomation> _logger;
    private readonly IGameWatcher _watcher;
    private readonly IGameModeService _gameMode;

    private bool _isEnabled;
    private bool _disposed;

    public GameModeAutomation(
        ILogger<GameModeAutomation> logger,
        IGameWatcher watcher,
        IGameModeService gameMode)
    {
        _logger = logger;
        _watcher = watcher;
        _gameMode = gameMode;

        _watcher.DetectionChanged += OnDetectionChanged;
    }

    /// <summary>Starts and stops the watcher. Off by default; the app sets it from the saved setting.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;

            if (value)
            {
                _watcher.Start();
            }
            else
            {
                _watcher.Stop();
            }

            _logger.LogInformation("Automatic game mode {State}", value ? "enabled" : "disabled");
        }
    }

    /// <summary>Raised after an automatic switch, so the interface can catch up.</summary>
    public event EventHandler? Switched;

    /// <summary>
    /// The work kicked off by the last detection. An event handler cannot await, so this is the
    /// only handle on it - without one a test has to sleep and hope, which is both slow and the
    /// kind of flake that gets a suite ignored.
    /// </summary>
    internal Task LastOperation { get; private set; } = Task.CompletedTask;

    private void OnDetectionChanged(object? sender, EventArgs e) => LastOperation = HandleAsync();

    private async Task HandleAsync()
    {
        if (!_isEnabled || _disposed)
        {
            return;
        }

        try
        {
            if (_watcher.DetectedGame is { } game)
            {
                if (_gameMode.IsActive)
                {
                    // Already on, by hand or from a previous game. Leave it alone rather than
                    // recording a second session over the first one's restore data.
                    return;
                }

                GameModeResult result = await _gameMode.EnableAsync(startedBy: GameModeTrigger.ForGame(game)).ConfigureAwait(false);
                Report(result, $"turn game mode on for {game.DisplayName}");
            }
            else
            {
                // Only undo what the automation did. A session the user switched on by hand
                // outlives the game that happened to be running.
                if (_gameMode.Session is not { AutoStarted: true })
                {
                    return;
                }

                GameModeResult result = await _gameMode.DisableAsync().ConfigureAwait(false);
                Report(result, "turn game mode off after the game exited");
            }

            Switched?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic game mode failed");
        }
    }

    private void Report(GameModeResult result, string what)
    {
        if (result.Result.Success)
        {
            _logger.LogInformation("Automation: {What} - done", what);
            return;
        }

        _logger.LogWarning("Automation could not {What}: {Message}", what, result.Result.Message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.DetectionChanged -= OnDetectionChanged;
    }
}
