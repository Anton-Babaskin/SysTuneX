using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tweaks;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Detection is edge-triggered, and that is the whole subtlety: firing on every poll would
/// re-enter game mode every few seconds for as long as the game is up, each time recording a
/// fresh session over the restore data of the last one.
/// </summary>
public sealed class GameWatcherTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "systunex-gametests-" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void Nothing_running_means_nothing_detected()
    {
        using GameWatcher watcher = Watcher(running: []);
        watcher.Poll();

        Assert.Null(watcher.DetectedGame);
    }

    [Fact]
    public void A_running_game_is_detected_and_reported_once()
    {
        using GameWatcher watcher = Watcher(running: ["dota2"]);

        int events = 0;
        watcher.DetectionChanged += (_, _) => events++;

        watcher.Poll();
        Assert.Equal("Dota 2", watcher.DetectedGame?.DisplayName);
        Assert.Equal(1, events);

        // Still running: no second event, because nothing changed.
        watcher.Poll();
        watcher.Poll();
        Assert.Equal(1, events);
    }

    [Fact]
    public void The_game_exiting_is_reported_once()
    {
        var running = new HashSet<string>(["dota2"], StringComparer.OrdinalIgnoreCase);
        using GameWatcher watcher = Watcher(running);

        int events = 0;
        watcher.DetectionChanged += (_, _) => events++;

        watcher.Poll();
        running.Clear();
        watcher.Poll();

        Assert.Null(watcher.DetectedGame);
        Assert.Equal(2, events);

        watcher.Poll();
        Assert.Equal(2, events);
    }

    [Fact]
    public async Task A_disabled_entry_is_ignored()
    {
        using GameWatcher watcher = Watcher(running: ["dota2"]);
        await watcher.SetEnabledAsync("dota2", enabled: false);

        watcher.Poll();

        Assert.Null(watcher.DetectedGame);
    }

    [Theory]
    [InlineData("dota2.exe", "dota2")]
    [InlineData("DOTA2.EXE", "DOTA2")]
    [InlineData("  cs2.exe  ", "cs2")]
    [InlineData("cs2", "cs2")]
    public void The_extension_is_stripped_however_it_is_typed(string typed, string expected) =>
        Assert.Equal(expected, GameWatcher.Normalize(typed));

    [Fact]
    public async Task A_custom_game_is_watched_like_any_other()
    {
        using GameWatcher watcher = Watcher(running: ["mygame"]);

        // Typed the way a person would, with the extension.
        Assert.True((await watcher.AddAsync("MyGame.exe", "My Game")).Success);

        watcher.Poll();
        Assert.Equal("My Game", watcher.DetectedGame?.DisplayName);
    }

    [Fact]
    public async Task Adding_the_same_game_twice_changes_nothing()
    {
        using GameWatcher watcher = Watcher(running: []);

        await watcher.AddAsync("mygame.exe", "My Game");
        var second = await watcher.AddAsync("MYGAME", "Something else");

        Assert.True(second.Success);
        Assert.False(second.Changed);
        Assert.Equal("Game_AlreadyWatched", second.Code);
        Assert.Single(watcher.Games, g => string.Equals(g.ProcessName, "mygame", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_empty_name_is_refused_rather_than_watched()
    {
        using GameWatcher watcher = Watcher(running: []);

        var result = await watcher.AddAsync("   ", "Nothing");

        Assert.False(result.Success);
        Assert.Equal("Game_NameEmpty", result.Code);
    }

    [Fact]
    public void The_built_in_list_is_loaded_and_free_of_duplicates()
    {
        using GameWatcher watcher = Watcher(running: []);

        Assert.NotEmpty(watcher.Games);
        Assert.Equal(GameCatalog.BuiltIn.Count, watcher.Games.Count);

        List<string> duplicates = watcher.Games
            .GroupBy(g => g.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Each watcher gets its own data directory. Sharing one would mean a test that adds a game
    /// or switches one off leaks into the next - which is exactly what happened the first time
    /// these ran against the real %ProgramData%.
    /// </summary>
    private GameWatcher Watcher(ICollection<string> running)
    {
        var watcher = new GameWatcher(NullLogger<GameWatcher>.Instance, _directory)
        {
            IsProcessRunning = name => running.Contains(name, StringComparer.OrdinalIgnoreCase),
        };

        watcher.LoadAsync().GetAwaiter().GetResult();
        return watcher;
    }
}
