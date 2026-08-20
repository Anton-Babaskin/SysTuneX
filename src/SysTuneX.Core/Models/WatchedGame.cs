namespace SysTuneX.Core.Models;

/// <summary>
/// An executable whose presence means "a game is running".
///
/// Matched on the process name without its extension, which is what Windows reports and what
/// stays stable when a launcher moves the install between drives.
/// </summary>
public sealed record WatchedGame
{
    /// <summary>Process name without ".exe", compared case-insensitively.</summary>
    public required string ProcessName { get; init; }

    /// <summary>What to call it in the interface.</summary>
    public required string DisplayName { get; init; }

    /// <summary>False for an entry the user switched off but did not delete.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Entries the user added themselves, which the built-in list must not overwrite.</summary>
    public bool IsCustom { get; init; }
}
