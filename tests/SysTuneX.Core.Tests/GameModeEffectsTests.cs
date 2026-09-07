using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The list the dashboard shows while game mode is on.
///
/// It lives in Core, and is tested, because the previous description of a session was built
/// inside the view model - where nothing could reach it, and where it had already drifted into
/// claiming things the session did not record.
/// </summary>
public sealed class GameModeEffectsTests
{
    private static readonly IReadOnlyList<ServiceDefinition> Catalogue =
    [
        new()
        {
            ServiceName = "DiagTrack",
            DisplayName = "Connected User Experiences and Telemetry",
            Description = "test",
            Risk = RiskLevel.Safe,
        },
    ];

    [Fact]
    public void No_session_describes_nothing()
    {
        Assert.Empty(GameModeEffects.Describe(null, Catalogue));
    }

    [Fact]
    public void The_power_scheme_leads_and_names_both_sides_of_the_swap()
    {
        IReadOnlyList<GameModeEffect> effects = GameModeEffects.Describe(
            new GameModeSession
            {
                StartedAt = DateTimeOffset.Now,
                ActivePowerSchemeName = "Ultimate Performance",
                PreviousPowerSchemeName = "Balanced",
                StoppedServices = ["DiagTrack"],
            },
            Catalogue);

        GameModeEffect power = effects[0];

        Assert.Equal(GameModeEffectKind.PowerScheme, power.Kind);
        Assert.Equal("Ultimate Performance", power.Subject);
        Assert.Equal("Balanced", power.Previous);
    }

    /// <summary>
    /// A session whose power switch failed records neither name. Listing it anyway would put an
    /// empty row on the card claiming a change that never happened.
    /// </summary>
    [Fact]
    public void A_power_scheme_that_never_changed_is_not_listed()
    {
        IReadOnlyList<GameModeEffect> effects = GameModeEffects.Describe(
            new GameModeSession { StartedAt = DateTimeOffset.Now, StoppedServices = ["DiagTrack"] },
            Catalogue);

        Assert.DoesNotContain(effects, e => e.Kind == GameModeEffectKind.PowerScheme);
    }

    [Fact]
    public void Services_are_shown_under_the_name_windows_uses()
    {
        IReadOnlyList<GameModeEffect> effects = GameModeEffects.Describe(
            new GameModeSession { StartedAt = DateTimeOffset.Now, StoppedServices = ["DiagTrack"] },
            Catalogue);

        GameModeEffect service = Assert.Single(effects, e => e.Kind == GameModeEffectKind.Service);

        Assert.Equal("Connected User Experiences and Telemetry", service.Subject);
        Assert.Equal("DiagTrack", service.Previous);
    }

    /// <summary>
    /// A session saved before a service left the catalogue still has to read. The short name is
    /// worse than the display name and far better than a blank row.
    /// </summary>
    [Fact]
    public void A_service_the_catalogue_no_longer_knows_falls_back_to_its_short_name()
    {
        IReadOnlyList<GameModeEffect> effects = GameModeEffects.Describe(
            new GameModeSession { StartedAt = DateTimeOffset.Now, StoppedServices = ["RetiredSvc"] },
            Catalogue);

        Assert.Equal("RetiredSvc", Assert.Single(effects).Subject);
    }

    [Fact]
    public void Memory_is_listed_when_something_was_actually_freed()
    {
        IReadOnlyList<GameModeEffect> effects = GameModeEffects.Describe(
            new GameModeSession { StartedAt = DateTimeOffset.Now, FreedMemoryMb = 2048 },
            Catalogue);

        GameModeEffect memory = Assert.Single(effects);

        Assert.Equal(GameModeEffectKind.Memory, memory.Kind);
        Assert.Equal(2048, memory.AmountMb);
    }

    [Fact]
    public void A_trim_that_freed_nothing_is_not_listed()
    {
        Assert.Empty(GameModeEffects.Describe(
            new GameModeSession { StartedAt = DateTimeOffset.Now, FreedMemoryMb = 0 },
            Catalogue));
    }

    /// <summary>
    /// The card promises that switching game mode off puts everything back. That promise is true
    /// of the services and the power scheme and false of the memory - Windows refills the standby
    /// list on its own, and there is nothing to restore. Saying otherwise would be the kind of
    /// small lie that costs the whole app its credibility.
    /// </summary>
    [Fact]
    public void Only_the_memory_trim_is_not_undone_on_exit()
    {
        IReadOnlyList<GameModeEffect> effects = GameModeEffects.Describe(
            new GameModeSession
            {
                StartedAt = DateTimeOffset.Now,
                ActivePowerSchemeName = "Ultimate Performance",
                PreviousPowerSchemeName = "Balanced",
                StoppedServices = ["DiagTrack"],
                FreedMemoryMb = 512,
            },
            Catalogue);

        Assert.Equal(3, effects.Count);
        Assert.All(
            effects.Where(e => e.Kind != GameModeEffectKind.Memory),
            e => Assert.True(e.IsRestoredOnExit));
        Assert.False(effects.Single(e => e.Kind == GameModeEffectKind.Memory).IsRestoredOnExit);
    }

    /// <summary>
    /// The real catalogue, not the two-entry one above: a display name that stops matching the
    /// service names game mode records would show the whole list as raw short names.
    /// </summary>
    [Fact]
    public void The_shipped_catalogue_resolves_the_services_game_mode_stops()
    {
        IReadOnlyList<GameModeEffect> effects = GameModeEffects.Describe(
            new GameModeSession { StartedAt = DateTimeOffset.Now, StoppedServices = ["DiagTrack", "WerSvc"] },
            ServiceCatalog.All);

        Assert.Equal(
            ["Connected User Experiences and Telemetry", "Windows Error Reporting"],
            effects.Select(e => e.Subject));
    }
}
