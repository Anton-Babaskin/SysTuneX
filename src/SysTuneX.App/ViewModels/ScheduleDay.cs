using CommunityToolkit.Mvvm.ComponentModel;

namespace SysTuneX.App.ViewModels;

/// <summary>One day chip in the schedule picker.</summary>
public sealed partial class ScheduleDay(DayOfWeek day, string label, bool selected) : ObservableObject
{
    [ObservableProperty]
    private bool _selected = selected;

    public DayOfWeek Day { get; } = day;

    public string Label { get; } = label;
}
