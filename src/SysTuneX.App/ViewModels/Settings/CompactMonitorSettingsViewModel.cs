using CommunityToolkit.Mvvm.ComponentModel;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>The small always-on-top readout, and the key that brings it up.</summary>
public sealed partial class CompactMonitorSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _host;
    private readonly ICompactMonitorService _compact;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompactSummary))]
    [NotifyPropertyChangedFor(nameof(CompactHotkeyStatus))]
    private bool _compactHotkeyEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompactSummary))]
    [NotifyPropertyChangedFor(nameof(CompactHotkeyStatus))]
    private string _compactHotkey = HotkeySpec.Default.ToString();

    [ObservableProperty]
    private bool _compactOpenOnStartup;

    public CompactMonitorSettingsViewModel(
        ISettingsHost host,
        ICompactMonitorService compact,
        ILocalizationService localization)
    {
        _host = host;
        _compact = compact;
        _localization = localization;

        _compact.StateChanged += (_, _) => OnPropertyChanged(nameof(CompactHotkeyStatus));
    }

    public void Load()
    {
        CompactMonitorSettings current = _host.Settings.CompactMonitor;

        CompactHotkeyEnabled = current.HotkeyEnabled;
        CompactHotkey = current.Hotkey;
        CompactOpenOnStartup = current.OpenOnStartup;
    }

    /// <summary>
    /// What the key is doing, in words: the combination Windows accepted, or why it did not.
    /// "Nothing happens when I press it" is the bug report this line exists to prevent.
    /// </summary>
    public string CompactHotkeyStatus
    {
        get
        {
            if (!CompactHotkeyEnabled)
            {
                return _localization["Settings_Summary_Off"];
            }

            bool parsed = HotkeySpec.TryParse(CompactHotkey, out HotkeySpec spec);

            return _compact.HotkeyFailure switch
            {
                HotkeyFailure.AlreadyTaken => _localization.Format("Compact_Hotkey_Taken", spec.ToString()),
                HotkeyFailure.UnknownKey => _localization.Format("Compact_Hotkey_UnknownKey", CompactHotkey),
                HotkeyFailure.Refused =>
                    _localization.Format("Compact_Hotkey_Refused", spec.ToString(), _compact.HotkeyErrorCode),

                // WindowNotReady means the window has not been shown yet, which the user never
                // sees; treat it like success rather than alarming them about a race they cannot
                // observe.
                _ => parsed
                    ? _localization.Format("Compact_Hotkey_Active", spec.ToString())
                    : _localization.Format("Compact_Hotkey_Fallback", CompactHotkey, spec.ToString()),
            };
        }
    }

    public string CompactSummary => CompactHotkeyEnabled
        ? CompactHotkey
        : _localization["Settings_Summary_Off"];

    partial void OnCompactHotkeyEnabledChanged(bool value)
    {
        if (_host.IsLoading)
        {
            return;
        }

        _host.Settings.CompactMonitor.HotkeyEnabled = value;
        _compact.ReapplyHotkey();
        _host.Save();
    }

    /// <summary>
    /// Stores what the user typed, not what it parsed to.
    ///
    /// Rewriting the box as they type takes the cursor with it and makes the field impossible to
    /// edit - two characters into "Ctrl+Alt+P" the text is already something else. What is stored
    /// is whatever they wrote; what is registered is what it parses to; and the status line below
    /// says which one Windows actually got, so a typo is visible rather than silently corrected.
    /// </summary>
    partial void OnCompactHotkeyChanged(string value)
    {
        if (_host.IsLoading)
        {
            return;
        }

        _host.Settings.CompactMonitor.Hotkey = value;
        _compact.ReapplyHotkey();
        _host.Save();
    }

    partial void OnCompactOpenOnStartupChanged(bool value)
    {
        if (_host.IsLoading)
        {
            return;
        }

        _host.Settings.CompactMonitor.OpenOnStartup = value;
        _host.Save();
    }

    /// <summary>Re-reads the words after a language change; the values themselves do not move.</summary>
    public void RefreshText()
    {
        OnPropertyChanged(nameof(CompactSummary));
        OnPropertyChanged(nameof(CompactHotkeyStatus));
    }
}
