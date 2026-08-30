using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// A switch that puts the machine into a gaming state and puts it back afterwards.
///
/// Deliberately different from applying a profile. A profile writes registry values that persist
/// across reboots and are undone from the change journal; game mode only does things that can be
/// undone immediately — stopping services rather than disabling them, switching the power scheme,
/// trimming memory. Nothing it does needs a reboot, so turning it off really does restore the
/// machine rather than leaving it half-tuned.
/// </summary>
public interface IGameModeService
{
    bool IsActive { get; }

    /// <summary>What the current session changed, or null when game mode is off.</summary>
    GameModeSession? Session { get; }

    /// <summary>Raised when the session starts or ends, so the UI can follow along.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Reads the session file. A session survives a restart of the app — and of Windows — so an
    /// interrupted session can still be turned off and restored rather than being forgotten.
    /// </summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <param name="startedBy">What caused this. Null means the user asked for it directly.</param>
    Task<GameModeResult> EnableAsync(
        IProgress<string>? progress = null,
        GameModeTrigger? startedBy = null,
        CancellationToken cancellationToken = default);

    Task<GameModeResult> DisableAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

/// <param name="StoppedServices">Services that were running and were stopped, to be started again on exit.</param>
/// <param name="PreviousPowerScheme">Scheme that was active before, restored on exit.</param>
public sealed record GameModeSession
{
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;

    public IReadOnlyList<string> StoppedServices { get; init; } = [];

    public Guid? PreviousPowerScheme { get; init; }

    public string PreviousPowerSchemeName { get; init; } = string.Empty;

    /// <summary>Megabytes the memory trim freed when the session started, for the UI to report.</summary>
    public long FreedMemoryMb { get; init; }

    /// <summary>
    /// Something other than the user turned this on. Only an automatic session is turned off
    /// automatically — switching it on by hand and having a game exit undo it would be rude.
    /// </summary>
    public bool AutoStarted { get; init; }

    /// <summary>Game that triggered an automatic session, for the interface to name. Empty for the schedule.</summary>
    public string TriggeredBy { get; init; } = string.Empty;

    /// <summary>
    /// What turned it on. Kept beside <see cref="AutoStarted"/> rather than replacing it so a
    /// session file written by an older build still reads back with its behaviour intact.
    /// </summary>
    public GameModeTriggerKind TriggerKind { get; init; } = GameModeTriggerKind.User;
}

/// <summary>Who asked for game mode. The distinction decides what may turn it off again.</summary>
public enum GameModeTriggerKind
{
    User,
    Game,
    Schedule,
}

/// <summary>
/// What started a session.
///
/// This exists because the schedule could not previously say so. The parameter was a
/// <see cref="WatchedGame"/>, the schedule had no game to pass, and so it passed nothing — which
/// left the session marked as user-started, and the schedule's own guard against closing a
/// hand-started session then refused to ever turn it off again.
/// </summary>
public sealed record GameModeTrigger
{
    public required GameModeTriggerKind Kind { get; init; }

    /// <summary>The game's name, when a game caused it.</summary>
    public string Name { get; init; } = string.Empty;

    public static GameModeTrigger ForGame(WatchedGame game) =>
        new() { Kind = GameModeTriggerKind.Game, Name = game.DisplayName };

    public static GameModeTrigger Schedule { get; } = new() { Kind = GameModeTriggerKind.Schedule };
}

/// <param name="Result">Whether the switch did what it said.</param>
/// <param name="ServicesAffected">Services stopped on enable, or started again on disable.</param>
/// <param name="Notes">Anything that did not work, named rather than swallowed.</param>
public sealed record GameModeResult(
    OperationResult Result,
    int ServicesAffected,
    long FreedMemoryMb,
    IReadOnlyList<string> Notes)
{
    public static GameModeResult Failed(string message) =>
        new(OperationResult.Fail(message), 0, 0, []);
}
