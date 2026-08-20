using SysTuneX.Core.Models;
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
