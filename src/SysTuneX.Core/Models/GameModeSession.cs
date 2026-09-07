namespace SysTuneX.Core.Models;

/// <summary>
/// What the running game mode session changed, and therefore what it owes the machine back.
///
/// Written to disk as JSON, so it outlives the app: a session interrupted by a crash, an update
/// or a reboot can still be turned off and restored rather than leaving services stopped with
/// nothing left that remembers stopping them. That is also why it lives beside the other models
/// rather than beside the interface - it is recorded state first and a return value second.
/// </summary>
public sealed record GameModeSession
{
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>Services that were running and were stopped, to be started again on exit.</summary>
    public IReadOnlyList<string> StoppedServices { get; init; } = [];

    /// <summary>Scheme that was active before, restored on exit.</summary>
    public Guid? PreviousPowerScheme { get; init; }

    public string PreviousPowerSchemeName { get; init; } = string.Empty;

    /// <summary>
    /// The scheme that is active for the duration of the session.
    ///
    /// Recorded because "high performance" is not one fixed name: it is Ultimate Performance where
    /// Windows offers it, a duplicate of it where Windows hides it, and High Performance on the
    /// editions that have neither. Naming the scheme it actually got beats claiming one.
    /// </summary>
    public string ActivePowerSchemeName { get; init; } = string.Empty;

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
