namespace SysTuneX.Core.Models;

/// <summary>
/// A window of the day during which game mode should be on.
///
/// Stored as local times of day rather than absolute timestamps: "every evening from 19:00"
/// is what someone means, and it has to survive the machine being asleep at 19:00.
/// </summary>
public sealed record GameModeSchedule
{
    public bool Enabled { get; init; }

    /// <summary>Local time the window opens.</summary>
    public TimeOnly StartsAt { get; init; } = new(19, 0);

    /// <summary>Local time it closes. Earlier than <see cref="StartsAt"/> means it runs past midnight.</summary>
    public TimeOnly EndsAt { get; init; } = new(23, 0);

    /// <summary>Days the window applies to. Empty means every day.</summary>
    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];

    /// <summary>Whether <paramref name="moment"/> falls inside the window.</summary>
    public bool Covers(DateTime moment)
    {
        if (!Enabled)
        {
            return false;
        }

        var time = TimeOnly.FromDateTime(moment);

        // A window that ends before it starts spans midnight, so the day it belongs to is the
        // day it *started* - Friday 23:00-02:00 is still Friday's window at half past midnight.
        if (EndsAt <= StartsAt)
        {
            return time >= StartsAt
                ? AppliesOn(moment.DayOfWeek)
                : time < EndsAt && AppliesOn(Previous(moment.DayOfWeek));
        }

        return time >= StartsAt && time < EndsAt && AppliesOn(moment.DayOfWeek);
    }

    private bool AppliesOn(DayOfWeek day) => Days.Count == 0 || Days.Contains(day);

    private static DayOfWeek Previous(DayOfWeek day) =>
        day == DayOfWeek.Sunday ? DayOfWeek.Saturday : day - 1;
}
