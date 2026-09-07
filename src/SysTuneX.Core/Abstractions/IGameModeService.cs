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
