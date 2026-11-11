using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using SysTuneX.Core.Tweaks;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The stores moved onto <see cref="JsonFile"/>, and three things have to be true of each of them
/// afterwards. A file written by the previous version still loads, because it is already sitting
/// on people's machines. A file cut off by a crash is set aside and logged, not silently replaced
/// by defaults and then overwritten. And a save leaves the store's file and nothing else.
///
/// The JSON here is written out by hand on purpose. Serialising a model to produce the fixture
/// would agree with any change to the model, which is exactly what these are meant to catch.
/// </summary>
public sealed class StoreFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "systunex-stores-" + Guid.NewGuid().ToString("N"));

    private FakeEnvironment Environment => new() { DataDirectory = _directory };

    // ---- gamemode.json ---------------------------------------------------------------------

    private const string GameModeFile = """
        {
          "StartedAt": "2026-10-31T21:40:12.3456789+03:00",
          "StoppedServices": [
            "DiagTrack",
            "SysMain"
          ],
          "PreviousPowerScheme": "381b4222-f694-41f0-9685-ff5bb260df2e",
          "PreviousPowerSchemeName": "Balanced",
          "ActivePowerSchemeName": "Ultimate Performance",
          "FreedMemoryMb": 734,
          "AutoStarted": true,
          "TriggeredBy": "Dota 2",
          "TriggerKind": 1
        }
        """;

    [Fact]
    public async Task A_game_mode_session_from_the_previous_version_still_loads()
    {
        await Plant("gamemode.json", GameModeFile);

        GameModeService gameMode = GameMode(NullLogger<GameModeService>.Instance);
        await gameMode.LoadAsync();

        Assert.True(gameMode.IsActive);
        GameModeSession session = gameMode.Session!;
        Assert.Equal(["DiagTrack", "SysMain"], session.StoppedServices);
        Assert.Equal(PowerScheme.Balanced, session.PreviousPowerScheme);
        Assert.Equal("Ultimate Performance", session.ActivePowerSchemeName);
        Assert.Equal(734, session.FreedMemoryMb);
        Assert.True(session.AutoStarted);
        Assert.Equal(GameModeTriggerKind.Game, session.TriggerKind);
        Assert.Equal(DateTimeOffset.Parse("2026-10-31T21:40:12.3456789+03:00", CultureInfo.InvariantCulture), session.StartedAt);
    }

    /// <summary>
    /// The case this work started from. Off is the only state the app can act on with a session it
    /// cannot read, but the file is the one record of which services are still stopped, so it
    /// must survive for whoever looks - not be overwritten by the next session.
    /// </summary>
    [Fact]
    public async Task A_truncated_game_mode_session_is_set_aside_not_overwritten()
    {
        await Plant("gamemode.json", Truncate(GameModeFile));
        var logger = new RecordingLogger<GameModeService>();

        GameModeService gameMode = GameMode(logger);
        await gameMode.LoadAsync();

        Assert.False(gameMode.IsActive);
        AssertSetAside("gamemode.json", Truncate(GameModeFile));
        Assert.Single(logger.Errors);

        // Turning game mode on again writes a fresh session beside the old one, not over it.
        await gameMode.EnableAsync();
        Assert.True(File.Exists(Path.Combine(_directory, "gamemode.json")));
        Assert.Single(Directory.GetFiles(_directory, "gamemode.json.corrupt-*"));
    }

    [Fact]
    public async Task Saving_a_game_mode_session_leaves_only_the_session_file()
    {
        FakeServiceManager services = new FakeServiceManager().Add("DiagTrack", RiskLevel.Safe, running: true);

        await GameMode(NullLogger<GameModeService>.Instance, services).EnableAsync();

        AssertOnly("gamemode.json");

        GameModeService afterRestart = GameMode(NullLogger<GameModeService>.Instance, services);
        await afterRestart.LoadAsync();
        Assert.Equal(["DiagTrack"], afterRestart.Session!.StoppedServices);
    }

    // ---- games.json ------------------------------------------------------------------------

    /// <summary>A built-in the user switched off, and one game of their own.</summary>
    private const string GamesFile = """
        [
          {
            "ProcessName": "dota2",
            "DisplayName": "Dota 2",
            "Enabled": false,
            "IsCustom": false
          },
          {
            "ProcessName": "MyIndieGame",
            "DisplayName": "My Indie Game",
            "Enabled": true,
            "IsCustom": true
          }
        ]
        """;

    [Fact]
    public async Task A_watched_game_list_from_the_previous_version_still_loads()
    {
        await Plant("games.json", GamesFile);

        using GameWatcher watcher = Watcher(NullLogger<GameWatcher>.Instance);
        await watcher.LoadAsync();

        Assert.False(watcher.Games.Single(g => g.ProcessName == "dota2").Enabled);

        WatchedGame own = watcher.Games.Single(g => g.ProcessName == "MyIndieGame");
        Assert.True(own.IsCustom);
        Assert.True(own.Enabled);
        Assert.Equal("My Indie Game", own.DisplayName);
    }

    [Fact]
    public async Task A_truncated_watched_game_list_is_set_aside_and_the_built_ins_are_used()
    {
        await Plant("games.json", Truncate(GamesFile));
        var logger = new RecordingLogger<GameWatcher>();

        using GameWatcher watcher = Watcher(logger);
        await watcher.LoadAsync();

        // What the file said - one built-in off, one game of the user's own - is not applied.
        Assert.Equal(GameCatalog.BuiltIn.Count, watcher.Games.Count);
        Assert.True(watcher.Games.Single(g => g.ProcessName == "dota2").Enabled);
        Assert.DoesNotContain(watcher.Games, g => g.IsCustom);
        AssertSetAside("games.json", Truncate(GamesFile));
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task Saving_the_watched_game_list_leaves_only_the_list()
    {
        using (GameWatcher watcher = Watcher(NullLogger<GameWatcher>.Instance))
        {
            await watcher.LoadAsync();
            await watcher.AddAsync("MyIndieGame.exe", "My Indie Game");
        }

        AssertOnly("games.json");

        using GameWatcher afterRestart = Watcher(NullLogger<GameWatcher>.Instance);
        await afterRestart.LoadAsync();
        Assert.Contains(afterRestart.Games, g => g.ProcessName == "MyIndieGame" && g.IsCustom);
    }

    // ---- snapshots.json --------------------------------------------------------------------

    private const string SnapshotsFile = """
        [
          {
            "Id": "0f1e2d3c4b5a69788796a5b4c3d2e1f0",
            "Label": "before Competitive FPS",
            "CapturedAt": "2026-10-30T18:00:00+03:00",
            "AppliedTweakIds": [],
            "RunningServices": [
              "DiagTrack",
              "SysMain"
            ],
            "PowerScheme": "381b4222-f694-41f0-9685-ff5bb260df2e",
            "PowerSchemeName": "Balanced",
            "RamUsedMb": 9120,
            "RamTotalMb": 32768,
            "StandbyMb": 4096,
            "ProcessCount": 241,
            "CpuUsagePercent": 7.5
          },
          {
            "Id": "a1b2c3d4e5f60718293a4b5c6d7e8f90",
            "Label": "after Competitive FPS",
            "CapturedAt": "2026-10-30T18:05:00+03:00",
            "AppliedTweakIds": [
              "gaming.hags"
            ],
            "RunningServices": [],
            "PowerScheme": "e9a42b02-d5df-448d-aa00-03f14749eb61",
            "PowerSchemeName": "Ultimate Performance",
            "RamUsedMb": 8010,
            "RamTotalMb": 32768,
            "StandbyMb": 1024,
            "ProcessCount": 229,
            "CpuUsagePercent": 4.25
          }
        ]
        """;

    [Fact]
    public async Task Snapshots_from_the_previous_version_still_load()
    {
        await Plant("snapshots.json", SnapshotsFile);

        SnapshotService snapshots = Snapshots(NullLogger<SnapshotService>.Instance);
        await snapshots.LoadAsync();

        Assert.Equal(["after Competitive FPS", "before Competitive FPS"], snapshots.Snapshots.Select(s => s.Label));

        SystemStateSnapshot before = snapshots.Snapshots[1];
        Assert.Equal(["DiagTrack", "SysMain"], before.RunningServices);
        Assert.Equal(PowerScheme.Balanced, before.PowerScheme);
        Assert.Equal(32768, before.RamTotalMb);
        Assert.Equal(241, before.ProcessCount);
        Assert.Equal(["gaming.hags"], snapshots.Snapshots[0].AppliedTweakIds);
        Assert.Equal(4.25, snapshots.Snapshots[0].CpuUsagePercent);
    }

    [Fact]
    public async Task Truncated_snapshots_are_set_aside_and_the_list_starts_empty()
    {
        await Plant("snapshots.json", Truncate(SnapshotsFile));
        var logger = new RecordingLogger<SnapshotService>();

        SnapshotService snapshots = Snapshots(logger);
        await snapshots.LoadAsync();

        Assert.Empty(snapshots.Snapshots);
        AssertSetAside("snapshots.json", Truncate(SnapshotsFile));
        Assert.Single(logger.Errors);
    }

    [Fact]
    public async Task Saving_snapshots_leaves_only_the_snapshot_file()
    {
        await Plant("snapshots.json", SnapshotsFile);

        SnapshotService snapshots = Snapshots(NullLogger<SnapshotService>.Instance);
        await snapshots.LoadAsync();
        await snapshots.DeleteAsync("0f1e2d3c4b5a69788796a5b4c3d2e1f0");

        AssertOnly("snapshots.json");

        SnapshotService afterRestart = Snapshots(NullLogger<SnapshotService>.Instance);
        await afterRestart.LoadAsync();
        Assert.Equal("after Competitive FPS", Assert.Single(afterRestart.Snapshots).Label);
    }

    // ---- profile.json ----------------------------------------------------------------------

    private const string ProfileFile = """
        {
          "ProfileId": "battle-royale",
          "AppliedAt": "2026-10-29T12:30:00+03:00",
          "IncludedAdvanced": true
        }
        """;

    [Fact]
    public async Task The_applied_profile_record_from_the_previous_version_still_loads()
    {
        await Plant("profile.json", ProfileFile);

        var store = new AppliedProfileStore(NullLogger<AppliedProfileStore>.Instance, Environment);

        Assert.NotNull(store.Current);
        Assert.Equal("battle-royale", store.Current.ProfileId);
        Assert.True(store.Current.IncludedAdvanced);
    }

    [Fact]
    public async Task A_truncated_applied_profile_record_is_set_aside_and_reads_as_none()
    {
        await Plant("profile.json", Truncate(ProfileFile));
        var logger = new RecordingLogger<AppliedProfileStore>();

        var store = new AppliedProfileStore(logger, Environment);

        Assert.Null(store.Current);
        AssertSetAside("profile.json", Truncate(ProfileFile));
        Assert.Single(logger.Errors);
    }

    [Fact]
    public void Saving_the_applied_profile_leaves_only_the_record()
    {
        new AppliedProfileStore(NullLogger<AppliedProfileStore>.Instance, Environment)
            .Write(new AppliedProfile { ProfileId = "esports", IncludedAdvanced = false });

        AssertOnly("profile.json");
        Assert.Equal("esports", new AppliedProfileStore(NullLogger<AppliedProfileStore>.Instance, Environment).Current?.ProfileId);
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>What a crash part way through a write used to leave: the first half of the file.</summary>
    private static string Truncate(string json) => json[..(json.Length / 2)];

    private async Task Plant(string name, string content)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, name), content);
    }

    /// <summary>The original is gone from its name and kept, byte for byte, under the corrupt one.</summary>
    private void AssertSetAside(string name, string content)
    {
        Assert.False(File.Exists(Path.Combine(_directory, name)));
        string corrupt = Assert.Single(Directory.GetFiles(_directory, name + ".corrupt-*"));
        Assert.Equal(content, File.ReadAllText(corrupt));
    }

    private void AssertOnly(string name) =>
        Assert.Equal([name], new DirectoryInfo(_directory).GetFiles().Select(f => f.Name));

    private GameModeService GameMode(ILogger<GameModeService> logger, FakeServiceManager? services = null) =>
        new(logger, Environment, services ?? new FakeServiceManager(), new FakePowerService(), new FakeProcessService());

    private GameWatcher Watcher(ILogger<GameWatcher> logger) =>
        new(logger, Environment) { IsProcessRunning = _ => false };

    private SnapshotService Snapshots(ILogger<SnapshotService> logger) =>
        new(logger, new FakeTweakEngine(), new FakeServiceManager(), new FakePowerService(), new NoSystemInfo(), Environment);

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

    /// <summary>Loading and saving snapshots never asks for live readings; capturing one would.</summary>
    private sealed class NoSystemInfo : ISystemInfoService
    {
        public Task<HardwareInfo> GetHardwareInfoAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not needed to load or save snapshots.");

        public SystemSnapshot GetSnapshot() =>
            throw new NotSupportedException("Not needed to load or save snapshots.");
    }
}
