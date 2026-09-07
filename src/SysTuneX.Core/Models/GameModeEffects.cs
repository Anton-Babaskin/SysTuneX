namespace SysTuneX.Core.Models;

/// <summary>What kind of change a line in the "currently in effect" list describes.</summary>
public enum GameModeEffectKind
{
    /// <summary>A service that was running and is stopped for the duration of the session.</summary>
    Service,

    /// <summary>The power scheme swap.</summary>
    PowerScheme,

    /// <summary>The one-off memory trim.</summary>
    Memory,
}

/// <summary>
/// One thing game mode is doing to the machine right now, and what happens to it on exit.
///
/// This exists because game mode reported itself through a toast that vanished after a few
/// seconds. It stops services, replaces the power scheme and frees gigabytes, and a minute later
/// there was nothing on screen saying so - which is why it read as "not clear what it does".
///
/// The text here is deliberately not translated: <see cref="Subject"/> and <see cref="Previous"/>
/// are names Windows chose ("Connected User Experiences and Telemetry", "Balanced"), and inventing
/// Russian for a service name Windows shows in English helps nobody. The sentence around them is
/// built from resources by the interface.
/// </summary>
public sealed record GameModeEffect
{
    public required GameModeEffectKind Kind { get; init; }

    /// <summary>The thing that changed: a service's display name, or the scheme now active.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>What it was before, when something was replaced. Empty when nothing was.</summary>
    public string Previous { get; init; } = string.Empty;

    /// <summary>Megabytes, for <see cref="GameModeEffectKind.Memory"/>. Zero everywhere else.</summary>
    public long AmountMb { get; init; }

    /// <summary>
    /// Switching game mode off puts this back. False for the memory trim: the pages it dropped
    /// are gone, and Windows refills the standby list on its own - claiming it is "restored"
    /// would be a comfortable lie.
    /// </summary>
    public bool IsRestoredOnExit => Kind is not GameModeEffectKind.Memory;
}

/// <summary>
/// Turns a session into the list the dashboard shows.
///
/// Deliberately in Core and deliberately pure: the interface used to hold the only description of
/// a session, which meant the description could not be tested and quietly drifted from what the
/// session actually recorded.
/// </summary>
public static class GameModeEffects
{
    /// <summary>
    /// Everything the session is holding, in the order it is worth reading: the power scheme
    /// first because it is the change with the largest effect on frame times, then the services
    /// by name, then the memory.
    /// </summary>
    /// <param name="catalogue">
    /// Used to turn a service's short name into the one Windows shows. Passed in rather than
    /// reached for so this namespace keeps depending on nothing, and so a test can describe a
    /// session without the real catalogue.
    /// </param>
    public static IReadOnlyList<GameModeEffect> Describe(
        GameModeSession? session,
        IReadOnlyList<ServiceDefinition> catalogue)
    {
        if (session is null)
        {
            return [];
        }

        var effects = new List<GameModeEffect>();

        // Only worth a line when we know what it moved to or away from. A session that failed to
        // switch the scheme records neither, and an empty "Power scheme: → " would be noise.
        if (session.ActivePowerSchemeName.Length > 0 || session.PreviousPowerSchemeName.Length > 0)
        {
            effects.Add(new GameModeEffect
            {
                Kind = GameModeEffectKind.PowerScheme,
                Subject = session.ActivePowerSchemeName,
                Previous = session.PreviousPowerSchemeName,
            });
        }

        foreach (string serviceName in session.StoppedServices)
        {
            // The catalogue's display name where there is one, the raw service name otherwise -
            // a session file written before a service left the catalogue still has to read.
            ServiceDefinition? definition = catalogue
                .FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));

            effects.Add(new GameModeEffect
            {
                Kind = GameModeEffectKind.Service,
                Subject = definition?.DisplayName ?? serviceName,
                Previous = serviceName,
            });
        }

        if (session.FreedMemoryMb > 0)
        {
            effects.Add(new GameModeEffect
            {
                Kind = GameModeEffectKind.Memory,
                AmountMb = session.FreedMemoryMb,
            });
        }

        return effects;
    }
}
