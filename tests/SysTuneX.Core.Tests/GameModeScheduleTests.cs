using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The window that runs past midnight is where this kind of feature usually breaks: 23:00-02:00
/// on Friday has to still be Friday's window at half past midnight, or a weekend schedule ends
/// an hour into the evening it was meant to cover.
/// </summary>
public sealed class GameModeScheduleTests
{
    private static readonly DateTime FridayNoon = new(2026, 8, 21, 12, 0, 0);

    [Fact]
    public void A_disabled_schedule_covers_nothing() =>
        Assert.False(new GameModeSchedule { Enabled = false }.Covers(FridayNoon.AddHours(8)));

    [Theory]
    [InlineData(18, 59, false)]
    [InlineData(19, 0, true)]
    [InlineData(21, 30, true)]
    [InlineData(22, 59, true)]
    [InlineData(23, 0, false)]
    public void An_ordinary_window_is_half_open(int hour, int minute, bool expected)
    {
        var schedule = new GameModeSchedule
        {
            Enabled = true,
            StartsAt = new TimeOnly(19, 0),
            EndsAt = new TimeOnly(23, 0),
        };

        Assert.Equal(expected, schedule.Covers(FridayNoon.Date.AddHours(hour).AddMinutes(minute)));
    }

    [Theory]
    [InlineData(22, 59, false)]
    [InlineData(23, 0, true)]
    [InlineData(23, 59, true)]
    public void A_window_across_midnight_covers_the_evening(int hour, int minute, bool expected) =>
        Assert.Equal(expected, Overnight().Covers(FridayNoon.Date.AddHours(hour).AddMinutes(minute)));

    [Theory]
    [InlineData(0, 30, true)]
    [InlineData(1, 59, true)]
    [InlineData(2, 0, false)]
    [InlineData(9, 0, false)]
    public void A_window_across_midnight_covers_the_small_hours(int hour, int minute, bool expected)
    {
        // Saturday morning, which belongs to Friday's window.
        DateTime saturday = FridayNoon.Date.AddDays(1).AddHours(hour).AddMinutes(minute);

        Assert.Equal(expected, Overnight().Covers(saturday));
    }

    [Fact]
    public void A_window_across_midnight_belongs_to_the_day_it_started()
    {
        // Friday-only, 23:00 to 02:00. Saturday 00:30 is inside it; Sunday 00:30 is not,
        // because Saturday evening was never covered.
        GameModeSchedule schedule = Overnight() with { Days = [DayOfWeek.Friday] };

        Assert.True(schedule.Covers(FridayNoon.Date.AddDays(1).AddMinutes(30)));
        Assert.False(schedule.Covers(FridayNoon.Date.AddDays(2).AddMinutes(30)));
    }

    [Fact]
    public void No_days_listed_means_every_day()
    {
        var schedule = new GameModeSchedule
        {
            Enabled = true,
            StartsAt = new TimeOnly(19, 0),
            EndsAt = new TimeOnly(23, 0),
            Days = [],
        };

        for (int day = 0; day < 7; day++)
        {
            Assert.True(schedule.Covers(FridayNoon.Date.AddDays(day).AddHours(20)));
        }
    }

    [Fact]
    public void A_day_that_is_not_listed_is_not_covered()
    {
        var weekend = new GameModeSchedule
        {
            Enabled = true,
            StartsAt = new TimeOnly(19, 0),
            EndsAt = new TimeOnly(23, 0),
            Days = [DayOfWeek.Saturday, DayOfWeek.Sunday],
        };

        Assert.False(weekend.Covers(FridayNoon.Date.AddHours(20)));
        Assert.True(weekend.Covers(FridayNoon.Date.AddDays(1).AddHours(20)));
        Assert.True(weekend.Covers(FridayNoon.Date.AddDays(2).AddHours(20)));
    }

    private static GameModeSchedule Overnight() => new()
    {
        Enabled = true,
        StartsAt = new TimeOnly(23, 0),
        EndsAt = new TimeOnly(2, 0),
    };
}

/// <summary>
/// The scheduler itself, rather than the window arithmetic.
///
/// Only <see cref="GameModeSchedule.Covers"/> had tests, and the schedule shipped with a defect
/// that no amount of testing Covers could have found: the scheduler turned game mode on and then
/// refused to turn it off again, leaving services stopped and the power plan raised until someone
/// noticed by hand.
/// </summary>
public sealed class GameModeSchedulerTests
{
    private static readonly DateTime Friday = new(2026, 8, 21, 0, 0, 0);

    private static (GameModeScheduler Scheduler, FakeGameModeService GameMode) Scheduler(
        DateTime now,
        FakeGameModeService? gameMode = null)
    {
        gameMode = gameMode ?? new FakeGameModeService();

        var scheduler = new GameModeScheduler(NullLogger<GameModeScheduler>.Instance, gameMode)
        {
            Now = () => now,
        };

        scheduler.Schedule = new GameModeSchedule
        {
            Enabled = true,
            StartsAt = new TimeOnly(19, 0),
            EndsAt = new TimeOnly(23, 0),
        };

        return (scheduler, gameMode);
    }

    [Fact]
    public async Task Entering_the_window_turns_game_mode_on()
    {
        (GameModeScheduler scheduler, FakeGameModeService gameMode) = Scheduler(Friday.AddHours(20));

        await scheduler.TickAsync();

        Assert.True(gameMode.IsActive);
        Assert.Equal([GameModeTriggerKind.Schedule], gameMode.EnabledBy);
    }

    [Fact]
    public async Task The_schedule_turns_off_what_the_schedule_turned_on()
    {
        // The defect, in the shape it actually had: one scheduler, ticked inside the window and
        // then outside it. Seeding an already-running session instead would have hidden it, since
        // the bug was in what the scheduler wrote when *it* enabled - the session read as
        // user-started, and the guard against closing a hand-started session then refused to ever
        // turn it off. Services stayed stopped and the power plan raised for as long as the
        // machine stayed up.
        var gameMode = new FakeGameModeService();
        DateTime now = Friday.AddHours(20);

        var scheduler = new GameModeScheduler(NullLogger<GameModeScheduler>.Instance, gameMode)
        {
            Now = () => now,
        };

        scheduler.Schedule = new GameModeSchedule
        {
            Enabled = true,
            StartsAt = new TimeOnly(19, 0),
            EndsAt = new TimeOnly(23, 0),
        };

        await scheduler.TickAsync();
        Assert.True(gameMode.IsActive);

        now = Friday.AddHours(23).AddMinutes(1);
        await scheduler.TickAsync();

        Assert.False(gameMode.IsActive);
        Assert.Equal(1, gameMode.DisableCount);
    }

    [Fact]
    public async Task A_session_switched_on_by_hand_survives_the_end_of_the_window()
    {
        // Someone who pressed the switch at 23:05 did not ask for it to end at 23:00.
        (GameModeScheduler scheduler, FakeGameModeService gameMode) = Scheduler(
            Friday.AddHours(23).AddMinutes(1),
            new FakeGameModeService().AlreadyOn(GameModeTriggerKind.User));

        await scheduler.TickAsync();

        Assert.True(gameMode.IsActive);
        Assert.Equal(0, gameMode.DisableCount);
    }

    [Fact]
    public async Task A_session_a_game_started_is_also_closed_by_the_window_ending()
    {
        (GameModeScheduler scheduler, FakeGameModeService gameMode) = Scheduler(
            Friday.AddHours(23).AddMinutes(1),
            new FakeGameModeService().AlreadyOn(GameModeTriggerKind.Game));

        await scheduler.TickAsync();

        Assert.False(gameMode.IsActive);
    }

    [Fact]
    public async Task Ticking_inside_the_window_twice_does_not_start_a_second_session()
    {
        (GameModeScheduler scheduler, FakeGameModeService gameMode) = Scheduler(Friday.AddHours(20));

        await scheduler.TickAsync();
        await scheduler.TickAsync();

        Assert.Single(gameMode.EnabledBy);
    }

    [Fact]
    public async Task Ticking_outside_the_window_with_game_mode_already_off_does_nothing()
    {
        (GameModeScheduler scheduler, FakeGameModeService gameMode) = Scheduler(Friday.AddHours(12));

        await scheduler.TickAsync();

        Assert.Empty(gameMode.EnabledBy);
        Assert.Equal(0, gameMode.DisableCount);
    }
}
