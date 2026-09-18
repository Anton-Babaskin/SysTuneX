using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;

namespace SysTuneX.App.ViewModels;

/// <summary>Hours of the week when game mode comes on by itself.</summary>
public sealed partial class ScheduleSettingsViewModel : ObservableObject
{
    /// <summary>Monday first, which is what a week looks like to most of the people using this.</summary>
    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private readonly ISettingsHost _host;
    private readonly GameModeScheduler _scheduler;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    private bool _scheduleEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    private string _scheduleStart = "19:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    private string _scheduleEnd = "23:00";

    public ScheduleSettingsViewModel(
        ISettingsHost host,
        GameModeScheduler scheduler,
        ILocalizationService localization)
    {
        _host = host;
        _scheduler = scheduler;
        _localization = localization;
    }

    public IReadOnlyList<ScheduleDay> ScheduleDays { get; private set; } = [];

    public string ScheduleSummary
    {
        get
        {
            if (!ScheduleEnabled)
            {
                return _localization["Settings_Summary_Off"];
            }

            string[] days = [.. ScheduleDays.Where(d => d.Selected).Select(d => d.Label)];

            return days.Length == 0
                ? _localization.Format("Settings_Summary_ScheduleNoDays", ScheduleStart, ScheduleEnd)
                : _localization.Format("Settings_Summary_Schedule", ScheduleStart, ScheduleEnd, string.Join(", ", days));
        }
    }

    public void Load()
    {
        GameModeSchedule current = _host.Settings.Schedule;

        ScheduleEnabled = current.Enabled;
        ScheduleStart = current.StartsAt.ToString("HH:mm");
        ScheduleEnd = current.EndsAt.ToString("HH:mm");

        ScheduleDays = [.. DayNames.Select((name, index) => new ScheduleDay(
            DayAt(index),
            name,
            current.Days.Contains(DayAt(index))))];

        OnPropertyChanged(nameof(ScheduleDays));

        // The three above raised ScheduleSummary before the days existed, so the summary they
        // produced said "no days selected". Re-read it now that they do.
        OnPropertyChanged(nameof(ScheduleSummary));
    }

    private static DayOfWeek DayAt(int index) => (DayOfWeek)(((int)DayOfWeek.Monday + index) % 7);

    partial void OnScheduleEnabledChanged(bool value) => Apply();

    partial void OnScheduleStartChanged(string value) => Apply();

    partial void OnScheduleEndChanged(string value) => Apply();

    [RelayCommand]
    private void ToggleScheduleDay(ScheduleDay? day)
    {
        if (day is null)
        {
            return;
        }

        day.Selected = !day.Selected;
        Apply();
    }

    /// <summary>
    /// Rebuilds the schedule from what is on screen. A time that will not parse leaves the
    /// stored one alone rather than resetting it to midnight while someone is mid-edit.
    /// </summary>
    private void Apply()
    {
        if (_host.IsLoading)
        {
            return;
        }

        GameModeSchedule current = _host.Settings.Schedule;

        var schedule = new GameModeSchedule
        {
            Enabled = ScheduleEnabled,
            StartsAt = ParseTime(ScheduleStart, current.StartsAt),
            EndsAt = ParseTime(ScheduleEnd, current.EndsAt),
            Days = [.. ScheduleDays.Where(d => d.Selected).Select(d => d.Day)],
        };

        _host.Settings.Schedule = schedule;
        _scheduler.Schedule = schedule;

        OnPropertyChanged(nameof(ScheduleSummary));
        _host.Save();
    }

    private static TimeOnly ParseTime(string text, TimeOnly fallback) =>
        TimeOnly.TryParse(text, CultureInfo.CurrentCulture, out TimeOnly parsed) ||
        TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : fallback;

    /// <summary>Re-reads the words after a language change; the values themselves do not move.</summary>
    public void RefreshText() => OnPropertyChanged(nameof(ScheduleSummary));
}
