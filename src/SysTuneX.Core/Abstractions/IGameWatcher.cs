using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// The list of executables SysTuneX watches for, and editing it.
///
/// The settings page's half. Split from the detector below because the two are asked for by
/// different callers for different reasons and neither wants the other's members: the settings
/// page never starts a poll, and the automation never adds a game.
/// </summary>
public interface IWatchedGameList
{
    IReadOnlyList<WatchedGame> Games { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds an executable to watch. The name may be given with or without ".exe".</summary>
    Task<OperationResult> AddAsync(string processName, string displayName, CancellationToken cancellationToken = default);

    Task RemoveAsync(string processName, CancellationToken cancellationToken = default);

    /// <summary>Turns an entry on or off without deleting it.</summary>
    Task SetEnabledAsync(string processName, bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>
/// Watches for a game starting and stopping, so game mode can follow it.
///
/// Only while SysTuneX is running: doing it with the app closed would mean a Windows service, and
/// a background service that stops other services is a much bigger thing to ask someone to trust
/// than a window they can see.
/// </summary>
public interface IGameDetector
{
    bool IsWatching { get; }

    /// <summary>The game currently running, or null when none of the watched executables is up.</summary>
    WatchedGame? DetectedGame { get; }

    /// <summary>Raised when a watched executable appears, and again when it goes away.</summary>
    event EventHandler? DetectionChanged;

    void Start();

    void Stop();
}

/// <summary>Both halves, for the one implementation and for the container.</summary>
public interface IGameWatcher : IWatchedGameList, IGameDetector, IDisposable;
