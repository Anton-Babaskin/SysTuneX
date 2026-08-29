using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The join between the watcher and game mode. One rule carries it: the automation only undoes
/// what the automation did. Getting that wrong means a session someone switched on by hand gets
/// torn down because an unrelated game happened to exit.
/// </summary>
public sealed class GameModeAutomationTests : IDisposable
{
    private static readonly WatchedGame Dota = new() { ProcessName = "dota2", DisplayName = "Dota 2" };

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "systunex-automation-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task A_game_starting_turns_game_mode_on()
    {
        (FakeGameWatcher watcher, GameModeService gameMode, GameModeAutomation automation) = Build();
        using (automation)
        {
            automation.IsEnabled = true;

            await DetectAsync(watcher, automation, Dota);

            Assert.True(gameMode.IsActive);
            Assert.True(gameMode.Session!.AutoStarted);
            Assert.Equal("Dota 2", gameMode.Session.TriggeredBy);
        }
    }

    [Fact]
    public async Task The_game_exiting_turns_it_off_again()
    {
        (FakeGameWatcher watcher, GameModeService gameMode, GameModeAutomation automation) = Build();
        using (automation)
        {
            automation.IsEnabled = true;

            await DetectAsync(watcher, automation, Dota);
            await DetectAsync(watcher, automation, null);

            Assert.False(gameMode.IsActive);
        }
    }

    /// <summary>The rule the whole feature turns on.</summary>
    [Fact]
    public async Task A_session_switched_on_by_hand_survives_a_game_exiting()
    {
        (FakeGameWatcher watcher, GameModeService gameMode, GameModeAutomation automation) = Build();
        using (automation)
        {
            automation.IsEnabled = true;

            // Switched on by the user, not by a game.
            await gameMode.EnableAsync();
            Assert.False(gameMode.Session!.AutoStarted);

            await DetectAsync(watcher, automation, Dota);
            await DetectAsync(watcher, automation, null);

            Assert.True(gameMode.IsActive);
        }
    }

    [Fact]
    public async Task A_hand_started_session_is_not_replaced_when_a_game_starts()
    {
        (FakeGameWatcher watcher, GameModeService gameMode, GameModeAutomation automation) = Build();
        using (automation)
        {
            automation.IsEnabled = true;

            await gameMode.EnableAsync();
            DateTimeOffset startedAt = gameMode.Session!.StartedAt;

            await DetectAsync(watcher, automation, Dota);

            // Re-entering would write a fresh session over the first one's restore data.
            Assert.Equal(startedAt, gameMode.Session!.StartedAt);
            Assert.False(gameMode.Session.AutoStarted);
        }
    }

    [Fact]
    public async Task Nothing_happens_while_the_automation_is_off()
    {
        (FakeGameWatcher watcher, GameModeService gameMode, GameModeAutomation automation) = Build();
        using (automation)
        {
            await DetectAsync(watcher, automation, Dota);

            Assert.False(gameMode.IsActive);
            Assert.False(watcher.IsWatching);
        }
    }

    [Fact]
    public void Switching_the_automation_on_and_off_starts_and_stops_the_watcher()
    {
        (FakeGameWatcher watcher, _, GameModeAutomation automation) = Build();
        using (automation)
        {
            Assert.False(watcher.IsWatching);

            automation.IsEnabled = true;
            Assert.True(watcher.IsWatching);

            automation.IsEnabled = false;
            Assert.False(watcher.IsWatching);
        }
    }

    [Fact]
    public async Task After_disposal_a_detection_is_ignored()
    {
        (FakeGameWatcher watcher, GameModeService gameMode, GameModeAutomation automation) = Build();
        automation.IsEnabled = true;
        automation.Dispose();

        await DetectAsync(watcher, automation, Dota);

        Assert.False(gameMode.IsActive);
    }

    /// <summary>
    /// The automation reacts to an event without awaiting, which is right for an event handler
    /// and awkward for a test. Awaiting the work it started beats sleeping and hoping.
    /// </summary>
    private static Task DetectAsync(FakeGameWatcher watcher, GameModeAutomation automation, WatchedGame? game)
    {
        watcher.Detect(game);
        return automation.LastOperation;
    }

    private (FakeGameWatcher Watcher, GameModeService GameMode, GameModeAutomation Automation) Build()
    {
        var watcher = new FakeGameWatcher();
        var gameMode = new GameModeService(
            NullLogger<GameModeService>.Instance,
            new FakeEnvironment(),
            new FakeServiceManager().Add("DiagTrack", RiskLevel.Safe, running: true),
            new FakePowerService(),
            new FakeProcessService(),
            _directory);

        var automation = new GameModeAutomation(NullLogger<GameModeAutomation>.Instance, watcher, gameMode);
        return (watcher, gameMode, automation);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // A leftover temp folder is not worth failing a test run over.
        }
    }
}
