using CommunityToolkit.Mvvm.ComponentModel;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// One tickable reading in the monitor's panel.
///
/// The tick reports upward rather than writing the selection itself, because the selection is what
/// enforces the cap - and a tick the cap refuses has to be put back, or the box would show on
/// while the reading stayed off.
/// </summary>
public sealed partial class MonitorMetricOption : ObservableObject
{
    private readonly Action<MonitorMetricOption, bool> _toggled;
    private bool _suppress;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>False when the cap is reached and this one is not already on.</summary>
    [ObservableProperty]
    private bool _canToggle = true;

    public MonitorMetricOption(
        MonitorMetricDefinition definition,
        string name,
        string groupName,
        bool isSelected,
        Action<MonitorMetricOption, bool> toggled)
    {
        Definition = definition;
        Name = name;
        GroupName = groupName;
        _isSelected = isSelected;
        _toggled = toggled;
    }

    public MonitorMetricDefinition Definition { get; }

    public string Name { get; }

    /// <summary>Grouped on by the panel, so the catalogue decides the headings rather than the XAML.</summary>
    public string GroupName { get; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (_suppress)
        {
            return;
        }

        _toggled(this, value);
    }

    /// <summary>Puts the tick back after a refusal, without reporting that as another toggle.</summary>
    public void RestoreTo(bool selected)
    {
        _suppress = true;
        try
        {
            IsSelected = selected;
        }
        finally
        {
            _suppress = false;
        }
    }
}
