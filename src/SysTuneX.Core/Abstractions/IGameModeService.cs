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

    Task<GameModeResult> EnableAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

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
