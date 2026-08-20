using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IGameWatcher"/>
[SupportedOSPlatform("windows")]
public sealed class GameWatcher : IGameWatcher
{
    /// <summary>
    /// Long enough that the poll costs nothing, short enough that game mode is on before the
    /// main menu finishes loading.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ILogger<GameWatcher> _logger;
    private readonly string _listFile;
    private readonly List<WatchedGame> _games = [];

    private CancellationTokenSource? _polling;
    private bool _disposed;

    /// <summary>
    /// Swapped out in tests. Asking for one name at a time is much cheaper than enumerating every
    /// process on the machine, which is what the old dashboard did on the UI thread.
    /// </summary>
    internal Func<string, bool> IsProcessRunning { get; set; } = DefaultProbe;

    /// <param name="dataDirectory">
    /// Defaults to %ProgramData%\SysTuneX. Injectable because a fixed path makes the watcher
    /// untestable: every test would read and write the real machine's list and the next one
    /// would inherit it.
    /// </param>
    public GameWatcher(ILogger<GameWatcher> logger, string? dataDirectory = null)
    {
        _logger = logger;
        _listFile = Path.Combine(dataDirectory ?? AppPaths.DataDirectory, "games.json");
    }

    public bool IsWatching => _polling is not null;

    public WatchedGame? DetectedGame { get; private set; }

    public IReadOnlyList<WatchedGame> Games
    {
        get
        {
            lock (_games)
            {
                return [.. _games];
            }
        }
    }

    public event EventHandler? DetectionChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        List<WatchedGame> saved = [];

        try
        {
            if (File.Exists(_listFile))
            {
                await using FileStream stream = File.OpenRead(_listFile);
                saved = await JsonSerializer
                    .DeserializeAsync<List<WatchedGame>>(stream, Json, cancellationToken)
                    .ConfigureAwait(false) ?? [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The watched game list could not be read: {Path}", _listFile);
        }

        lock (_games)
        {
            _games.Clear();

            // The built-in list is the baseline; the saved file only carries the user's own
            // entries and the on/off state, so shipping a new built-in game reaches everyone.
            foreach (WatchedGame game in GameCatalog.BuiltIn)
            {
                WatchedGame? stored = saved.FirstOrDefault(g => Matches(g.ProcessName, game.ProcessName));
                _games.Add(stored is null ? game : game with { Enabled = stored.Enabled });
            }

            _games.AddRange(saved.Where(g => g.IsCustom));
        }
    }

    public async Task<OperationResult> AddAsync(
        string processName,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        string normalized = Normalize(processName);

        if (normalized.Length == 0)
        {
            return OperationResult.Fail(CoreMessages.GameNameEmpty);
        }

        lock (_games)
        {
            if (_games.Any(g => Matches(g.ProcessName, normalized)))
            {
                return OperationResult.NoChange(CoreMessages.GameAlreadyWatched, normalized);
            }

            _games.Add(new WatchedGame
            {
                ProcessName = normalized,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalized : displayName.Trim(),
                IsCustom = true,
            });
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Watching {Process} for game mode", normalized);
        return OperationResult.Ok();
    }

    public async Task RemoveAsync(string processName, CancellationToken cancellationToken = default)
    {
        lock (_games)
        {
            _games.RemoveAll(g => Matches(g.ProcessName, processName) && g.IsCustom);
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(string processName, bool enabled, CancellationToken cancellationToken = default)
    {
        lock (_games)
        {
            for (int i = 0; i < _games.Count; i++)
            {
                if (Matches(_games[i].ProcessName, processName))
                {
                    _games[i] = _games[i] with { Enabled = enabled };
                }
            }
        }

        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Start()
    {
        if (_disposed || _polling is not null)
        {
            return;
        }

        _polling = new CancellationTokenSource();
        _ = PollAsync(_polling.Token);
        _logger.LogInformation("Watching {Count} executables for game mode", Games.Count(g => g.Enabled));
    }

    public void Stop()
    {
        CancellationTokenSource? polling = Interlocked.Exchange(ref _polling, null);
        if (polling is null)
        {
            return;
        }

        polling.Cancel();
        polling.Dispose();

        if (DetectedGame is not null)
        {
            DetectedGame = null;
            DetectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>One pass over the watch list. Exposed so tests can drive detection deterministically.</summary>
    internal void Poll()
    {
        WatchedGame? found = null;

        foreach (WatchedGame game in Games)
        {
            if (!game.Enabled)
            {
                continue;
            }

            try
            {
                if (IsProcessRunning(game.ProcessName))
                {
                    found = game;
                    break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not probe for {Process}", game.ProcessName);
            }
        }

        // Only the transition matters. Firing on every poll would re-enter game mode every five
        // seconds for as long as the game is up.
        if (Matches(found?.ProcessName, DetectedGame?.ProcessName))
        {
            return;
        }

        DetectedGame = found;

        _logger.LogInformation(
            found is null ? "Watched game exited" : "Watched game started: {Game}",
            found?.DisplayName);

        DetectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            // Once immediately, so launching SysTuneX with a game already up is noticed.
            Poll();

            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Poll();
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() was called.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The game watcher stopped unexpectedly");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_listFile)!);

            // Only what the user decided: their own entries, and any built-in they switched off.
            List<WatchedGame> persist;
            lock (_games)
            {
                persist = [.. _games.Where(g => g.IsCustom || !g.Enabled)];
            }

            await using FileStream stream = File.Create(_listFile);
            await JsonSerializer.SerializeAsync(stream, persist, Json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The watched game list could not be saved");
        }
    }

    private static bool DefaultProbe(string processName)
    {
        Process[] found = Process.GetProcessesByName(processName);
        try
        {
            return found.Length > 0;
        }
        finally
        {
            foreach (Process process in found)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Windows reports process names without the extension and ignores case.</summary>
    internal static string Normalize(string processName)
    {
        string trimmed = (processName ?? string.Empty).Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^4]
            : trimmed;
    }

    private static bool Matches(string? left, string? right) =>
        string.Equals(Normalize(left ?? string.Empty), Normalize(right ?? string.Empty), StringComparison.OrdinalIgnoreCase);

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
