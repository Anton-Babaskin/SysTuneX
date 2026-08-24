using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Game mode's entire promise is that switching it off puts the machine back. A bug here does
/// not show up as a crash - it shows up as a user whose search and telemetry services are still
/// stopped a week later, with nothing on screen saying so.
/// </summary>
public sealed class GameModeServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "systunex-gamemode-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Enabling_stops_only_the_safe_services_that_were_running()
    {
        FakeServiceManager services = new FakeServiceManager()
            .Add("DiagTrack", RiskLevel.Safe, running: true)
            .Add("SysMain", RiskLevel.Safe, running: true)
            .Add("AlreadyStopped", RiskLevel.Safe, running: false)
            .Add("WSearch", RiskLevel.Moderate, running: true)
            .Add("BitLocker", RiskLevel.Advanced, running: true);

        GameModeService gameMode = Service(services);

        GameModeResult result = await gameMode.EnableAsync();

        Assert.True(result.Result.Success);
        Assert.Equal(["DiagTrack", "SysMain"], services.Stopped);
        Assert.Equal(2, result.ServicesAffected);
    }

    [Fact]
    public async Task Turning_it_off_starts_back_exactly_what_it_stopped()
    {
        FakeServiceManager services = new FakeServiceManager()
            .Add("DiagTrack", RiskLevel.Safe, running: true)
            .Add("AlreadyStopped", RiskLevel.Safe, running: false);

        GameModeService gameMode = Service(services);

        await gameMode.EnableAsync();
        GameModeResult off = await gameMode.DisableAsync();

        Assert.True(off.Result.Success);

        // AlreadyStopped was stopped before game mode ran, so starting it would be a change the
        // user never asked for - the classic way a "restore" leaves a machine different.
        Assert.Equal(["DiagTrack"], services.Started);
        Assert.False(gameMode.IsActive);
    }

    [Fact]
    public async Task The_previous_power_scheme_comes_back()
    {
        var power = new FakePowerService { ActiveScheme = PowerScheme.Balanced };
        GameModeService gameMode = Service(new FakeServiceManager(), power);

        await gameMode.EnableAsync();
        Assert.Equal(PowerScheme.UltimatePerformance, power.ActiveScheme);

        await gameMode.DisableAsync();
        Assert.Equal(PowerScheme.Balanced, power.ActiveScheme);
    }

    [Fact]
    public async Task Memory_is_trimmed_once_per_session()
    {
        var processes = new FakeProcessService();
        GameModeService gameMode = Service(new FakeServiceManager(), processes: processes);

        await gameMode.EnableAsync();
        await gameMode.EnableAsync();

        Assert.Equal(1, processes.TrimCount);
    }

    [Fact]
    public async Task Enabling_twice_is_not_a_second_session()
    {
        FakeServiceManager services = new FakeServiceManager().Add("DiagTrack", RiskLevel.Safe, running: true);
        GameModeService gameMode = Service(services);

        await gameMode.EnableAsync();
        GameModeResult second = await gameMode.EnableAsync();

        Assert.True(second.Result.Success);
        Assert.False(second.Result.Changed);
        Assert.Equal("GameMode_AlreadyOn", second.Result.Code);

        // The point: a second session would have recorded an empty stopped-services list over
        // the first one's, and DiagTrack would never be started again.
        await gameMode.DisableAsync();
        Assert.Equal(["DiagTrack"], services.Started);
    }

    [Fact]
    public async Task Turning_it_off_when_it_is_not_on_changes_nothing()
    {
        GameModeResult result = await Service(new FakeServiceManager()).DisableAsync();

        Assert.True(result.Result.Success);
        Assert.False(result.Result.Changed);
        Assert.Equal("GameMode_NotOn", result.Result.Code);
    }

    [Fact]
    public async Task Without_administrator_rights_it_refuses_rather_than_half_working()
    {
        var environment = new FakeEnvironment { IsElevated = false };
        FakeServiceManager services = new FakeServiceManager().Add("DiagTrack", RiskLevel.Safe, running: true);

        GameModeResult result = await Service(services, environment: environment).EnableAsync();

        Assert.False(result.Result.Success);
        Assert.Equal("GameMode_NeedsAdministrator", result.Result.Code);
        Assert.Empty(services.Stopped);
    }

    /// <summary>
    /// A service that will not stop is worth knowing about before blaming the game, so it is
    /// named in the notes rather than swallowed.
    /// </summary>
    [Fact]
    public async Task A_service_that_refuses_to_stop_is_reported_and_not_recorded()
    {
        FakeServiceManager services = new FakeServiceManager()
            .Add("DiagTrack", RiskLevel.Safe, running: true)
            .Add("Stubborn", RiskLevel.Safe, running: true);

        services.RefusesToStop.Add("Stubborn");

        GameModeService gameMode = Service(services);
        GameModeResult result = await gameMode.EnableAsync();

        Assert.Contains(result.Notes, note => note.Contains("Stubborn", StringComparison.Ordinal));
        Assert.Equal(["DiagTrack"], services.Stopped);

        // It was never stopped, so it must not be "started again" on the way out.
        await gameMode.DisableAsync();
        Assert.Equal(["DiagTrack"], services.Started);
    }

    [Fact]
    public async Task A_service_that_refuses_to_start_again_is_reported()
    {
        FakeServiceManager services = new FakeServiceManager().Add("DiagTrack", RiskLevel.Safe, running: true);
        services.RefusesToStart.Add("DiagTrack");

        GameModeService gameMode = Service(services);
        await gameMode.EnableAsync();

        GameModeResult off = await gameMode.DisableAsync();

        Assert.Contains(off.Notes, note => note.Contains("DiagTrack", StringComparison.Ordinal));

        // The session still ends. Leaving it "on" forever because one service refused would
        // strand the other nine.
        Assert.False(gameMode.IsActive);
    }

    /// <summary>
    /// The failure that matters most: the app dies mid-session. A new instance has to find the
    /// session on disk and still be able to put the machine back.
    /// </summary>
    [Fact]
    public async Task An_interrupted_session_survives_a_restart_and_can_still_be_undone()
    {
        FakeServiceManager services = new FakeServiceManager()
            .Add("DiagTrack", RiskLevel.Safe, running: true)
            .Add("SysMain", RiskLevel.Safe, running: true);

        var power = new FakePowerService { ActiveScheme = PowerScheme.Balanced };

        await Service(services, power).EnableAsync();

        // A brand new instance, as after a crash and relaunch.
        GameModeService afterRestart = Service(services, power);
        Assert.False(afterRestart.IsActive);

        await afterRestart.LoadAsync();

        Assert.True(afterRestart.IsActive);
        Assert.Equal(["DiagTrack", "SysMain"], afterRestart.Session!.StoppedServices);

        await afterRestart.DisableAsync();

        Assert.Equal(["DiagTrack", "SysMain"], services.Started);
        Assert.Equal(PowerScheme.Balanced, power.ActiveScheme);
    }

    [Fact]
    public async Task A_session_started_by_a_game_records_what_started_it()
    {
        GameModeService gameMode = Service(new FakeServiceManager());

        var dota = new WatchedGame { ProcessName = "dota2", DisplayName = "Dota 2" };
        await gameMode.EnableAsync(trigger: dota);

        Assert.True(gameMode.Session!.AutoStarted);
        Assert.Equal("Dota 2", gameMode.Session.TriggeredBy);
    }

    [Fact]
    public async Task A_session_started_by_the_user_says_so()
    {
        GameModeService gameMode = Service(new FakeServiceManager());
        await gameMode.EnableAsync();

        Assert.False(gameMode.Session!.AutoStarted);
        Assert.Empty(gameMode.Session.TriggeredBy);
    }

    /// <summary>
    /// An edition with no high performance scheme should still get the rest of game mode, with
    /// the power failure named - not lose the whole feature over one unavailable scheme.
    /// </summary>
    [Fact]
    public async Task An_edition_without_a_high_performance_scheme_still_stops_services()
    {
        FakeServiceManager services = new FakeServiceManager().Add("DiagTrack", RiskLevel.Safe, running: true);
        var power = new FakePowerService { HighPerformanceAvailable = false };

        GameModeResult result = await Service(services, power).EnableAsync();

        Assert.True(result.Result.Success);
        Assert.Equal(["DiagTrack"], services.Stopped);
        Assert.NotEmpty(result.Notes);
    }

    private GameModeService Service(
        FakeServiceManager services,
        FakePowerService? power = null,
        FakeProcessService? processes = null,
        FakeEnvironment? environment = null) =>
        new(
            NullLogger<GameModeService>.Instance,
            environment ?? new FakeEnvironment(),
            services,
            power ?? new FakePowerService(),
            processes ?? new FakeProcessService(),
            _directory);

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
