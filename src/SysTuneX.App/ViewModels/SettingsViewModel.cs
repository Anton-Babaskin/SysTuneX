using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SysTuneX.App.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    private const string RepositoryUrl = "https://github.com/Anton-Babaskin/SysTuneX";

    private readonly IAppSettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly IEnvironmentService _environment;
    private readonly IUserInteraction _interaction;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IPowerSchemeService _power;
    private readonly IWatchedGameList _watcher;
    private readonly GameModeAutomation _automation;
    private readonly ITrayIconService _tray;
    private readonly GameModeScheduler _scheduler;
    private readonly ICompactMonitorService _compact;
    private readonly IShellLauncher _shell;
    private readonly IWindowAppearance _appearance;
    private readonly ILogger<SettingsViewModel> _logger;

    private bool _isLoading = true;

    [ObservableProperty]
    private string _selectedTheme = "system";

    [ObservableProperty]
    private string _selectedBackdrop = "Mica";

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private bool _createRestorePoint;

    [ObservableProperty]
    private bool _confirmAdvanced;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticsSummary))]
    private bool _verboseLogging;

    [ObservableProperty]
    private string? _lastReportPath;

    [ObservableProperty]
    private bool _isBuildingReport;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PowerSummary))]
    private PowerScheme? _selectedPowerScheme;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoGameModeSummary))]
    private bool _autoGameMode;

    [ObservableProperty]
    private string _newGameName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMinimizeToTray))]
    [NotifyPropertyChangedFor(nameof(TraySummary))]
    private bool _showTrayIcon = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TraySummary))]
    private bool _minimizeToTray;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    private bool _scheduleEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    private string _scheduleStart = "19:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    private string _scheduleEnd = "23:00";

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

    public SettingsViewModel(
        IAppSettingsService settings,
        ILocalizationService localization,
        IEnvironmentService environment,
        IUserInteraction interaction,
        IDiagnosticsService diagnostics,
        IPowerSchemeService power,
        IWatchedGameList watcher,
        GameModeAutomation automation,
        ITrayIconService tray,
        GameModeScheduler scheduler,
        ICompactMonitorService compact,
        IShellLauncher shell,
        IWindowAppearance appearance,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _localization = localization;
        _environment = environment;
        _interaction = interaction;
        _diagnostics = diagnostics;
        _power = power;
        _watcher = watcher;
        _automation = automation;
        _tray = tray;
        _scheduler = scheduler;
        _compact = compact;
        _shell = shell;
        _appearance = appearance;
        _logger = logger;

        _compact.StateChanged += (_, _) => OnPropertyChanged(nameof(CompactHotkeyStatus));

        Languages = localization.AvailableLanguages;
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public ObservableCollection<PowerScheme> PowerSchemes { get; } = [];

    public ObservableCollection<WatchedGame> WatchedGames { get; } = [];

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";

    public string DataDirectory => _environment.DataDirectory;

    public string LogDirectory => _diagnostics.LogDirectory;

    public string WindowsDescription => _environment.Windows.ToString();

    public string ElevationDescription =>
        _environment.IsElevated ? _localization["Dashboard_Elevated"] : _localization["Banner_NotElevated_Title"];

    protected override Task OnEnterAsync()
    {
        _isLoading = true;

        AppSettings current = _settings.Current;

        SelectedTheme = current.Theme switch
        {
            ApplicationTheme.Light => "light",
            ApplicationTheme.Dark => "dark",
            _ => "system",
        };

        SelectedBackdrop = current.Backdrop.ToString();
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == current.Language) ?? Languages[0];
        CreateRestorePoint = current.CreateRestorePointBeforeProfiles;
        ConfirmAdvanced = current.ConfirmAdvancedChanges;
        VerboseLogging = current.VerboseLogging;
        AutoGameMode = current.AutoGameMode;
        ShowTrayIcon = current.ShowTrayIcon;
        MinimizeToTray = current.MinimizeToTray;
        CompactHotkeyEnabled = current.CompactMonitor.HotkeyEnabled;
        CompactHotkey = current.CompactMonitor.Hotkey;
        CompactOpenOnStartup = current.CompactMonitor.OpenOnStartup;
        ScheduleEnabled = current.Schedule.Enabled;
        ScheduleStart = current.Schedule.StartsAt.ToString("HH:mm");
        ScheduleEnd = current.Schedule.EndsAt.ToString("HH:mm");
        ScheduleDays = [.. ScheduleDayNames.Select((name, index) => new ScheduleDay(
            (DayOfWeek)(((int)DayOfWeek.Monday + index) % 7),
            name,
            current.Schedule.Days.Contains((DayOfWeek)(((int)DayOfWeek.Monday + index) % 7))))];
        OnPropertyChanged(nameof(ScheduleDays));

        // ScheduleEnabled and the two times raised ScheduleSummary above, before the days existed,
        // so the summary they produced said "no days selected". Re-read it now that they do.
        RefreshSummaries();

        _isLoading = false;

        RefreshWatchedGames();
        return LoadPowerSchemesAsync();
    }

    /// <summary>
    /// Reads the schemes registered on this machine rather than assuming the three well-known
    /// GUIDs: OEMs ship their own, and Ultimate Performance only exists once something has
    /// duplicated it.
    /// </summary>
    private async Task LoadPowerSchemesAsync()
    {
        try
        {
            IReadOnlyList<PowerScheme> schemes = await _power.GetSchemesAsync().ConfigureAwait(true);

            PowerSchemes.Clear();
            foreach (PowerScheme scheme in schemes)
            {
                PowerSchemes.Add(scheme);
            }

            SelectedPowerScheme = schemes.FirstOrDefault(s => s.IsActive);
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    [RelayCommand]
    private async Task ApplyPowerSchemeAsync()
    {
        if (SelectedPowerScheme is not { } scheme || scheme.IsActive)
        {
            return;
        }

        OperationResult result = await _power.SetActiveSchemeAsync(scheme.Guid).ConfigureAwait(true);

        if (!result.Success)
        {
            _interaction.ShowError(result.Describe(_localization));
            return;
        }

        _interaction.ShowSuccess(string.Format(_localization["Settings_PowerPlan_Done"], scheme.Name));

        // Re-read rather than assume: the scheme that ends up active is the one powercfg says is.
        await LoadPowerSchemesAsync().ConfigureAwait(true);
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_isLoading)
        {
            return;
        }

        ApplicationTheme theme = value switch
        {
            "light" => ApplicationTheme.Light,
            "dark" => ApplicationTheme.Dark,
            _ => ApplicationTheme.Unknown,
        };

        _settings.Current.Theme = theme;
        _appearance.ApplyTheme(theme);

        Save();
    }

    partial void OnSelectedBackdropChanged(string value)
    {
        if (_isLoading || !Enum.TryParse(value, out WindowBackdropType backdrop))
        {
            return;
        }

        _settings.Current.Backdrop = backdrop;

        // The preference is saved either way; only the live change is allowed to fail. Saying so
        // is the difference between "this takes effect next launch" and a button that did nothing.
        if (!_appearance.ApplyBackdrop(backdrop))
        {
            _interaction.ShowInfo(_localization["Settings_Backdrop_NeedsRestart"]);
        }

        Save();
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (_isLoading || value is null)
        {
            return;
        }

        _settings.Current.Language = value.Code;
        _localization.SetLanguage(value.Code);
        OnPropertyChanged(nameof(ElevationDescription));
        OnPropertyChanged(nameof(TraySummary));
        OnPropertyChanged(nameof(PowerSummary));
        OnPropertyChanged(nameof(DiagnosticsSummary));
        RefreshSummaries();
        Save();
    }

    partial void OnCreateRestorePointChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.Current.CreateRestorePointBeforeProfiles = value;
        Save();
    }

    partial void OnConfirmAdvancedChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.Current.ConfirmAdvancedChanges = value;
        Save();
    }

    // ── The compact readout ──────────────────────────────────────────────────

    partial void OnCompactHotkeyEnabledChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.Current.CompactMonitor.HotkeyEnabled = value;
        _compact.ReapplyHotkey();
        Save();
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
        if (_isLoading)
        {
            return;
        }

        _settings.Current.CompactMonitor.Hotkey = value;
        _compact.ReapplyHotkey();
        Save();
    }

    partial void OnCompactOpenOnStartupChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.Current.CompactMonitor.OpenOnStartup = value;
        Save();
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


    // ── Collapsed-row summaries ──────────────────────────────────────────────
    //
    // Everything below the first two cards is folded away, which only works if a folded row still
    // says what it is set to. Otherwise simplifying the page just means hiding it, and someone has
    // to open all six to find the one they changed last week.

    public string TraySummary => !ShowTrayIcon
        ? _localization["Settings_Summary_Off"]
        : MinimizeToTray
            ? _localization["Settings_Summary_TrayAndClose"]
            : _localization["Settings_Summary_On"];

    public string AutoGameModeSummary => AutoGameMode
        ? _localization.Format("Settings_Summary_GamesWatched", WatchedGames.Count(g => g.Enabled))
        : _localization["Settings_Summary_Off"];

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

    public string PowerSummary => SelectedPowerScheme?.Name ?? _localization["Settings_Summary_Unknown"];

    public string DiagnosticsSummary => VerboseLogging
        ? _localization["Settings_Summary_VerboseOn"]
        : _localization["Settings_Summary_VerboseOff"];

    /// <summary>Raised together because the schedule and the game list feed two of the summaries.</summary>
    private void RefreshSummaries()
    {
        OnPropertyChanged(nameof(ScheduleSummary));
        OnPropertyChanged(nameof(AutoGameModeSummary));
    }

    /// <summary>Closing to the tray only makes sense while there is a tray icon to come back from.</summary>
    public bool CanMinimizeToTray => ShowTrayIcon;

    /// <summary>Monday first, which is what a week looks like to most of the people using this.</summary>
    private static readonly string[] ScheduleDayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    public IReadOnlyList<ScheduleDay> ScheduleDays { get; private set; } = [];

    partial void OnScheduleEnabledChanged(bool value) => ApplySchedule();

    partial void OnScheduleStartChanged(string value) => ApplySchedule();

    partial void OnScheduleEndChanged(string value) => ApplySchedule();

    [RelayCommand]
    private void ToggleScheduleDay(ScheduleDay? day)
    {
        if (day is null)
        {
            return;
        }

        day.Selected = !day.Selected;
        ApplySchedule();
    }

    /// <summary>
    /// Rebuilds the schedule from what is on screen. A time that will not parse leaves the
    /// stored one alone rather than resetting it to midnight while someone is mid-edit.
    /// </summary>
    private void ApplySchedule()
    {
        if (_isLoading)
        {
            return;
        }

        GameModeSchedule current = _settings.Current.Schedule;

        var schedule = new GameModeSchedule
        {
            Enabled = ScheduleEnabled,
            StartsAt = ParseTime(ScheduleStart, current.StartsAt),
            EndsAt = ParseTime(ScheduleEnd, current.EndsAt),
            Days = [.. ScheduleDays.Where(d => d.Selected).Select(d => d.Day)],
        };

        _settings.Current.Schedule = schedule;
        _scheduler.Schedule = schedule;
        RefreshSummaries();
        Save();
    }

    private static TimeOnly ParseTime(string text, TimeOnly fallback) =>
        TimeOnly.TryParse(text, CultureInfo.CurrentCulture, out TimeOnly parsed) ||
        TimeOnly.TryParse(text, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : fallback;

    partial void OnShowTrayIconChanged(bool value)
    {
        if (value)
        {
            _tray.Show();
        }
        else
        {
            _tray.Hide();

            // Otherwise closing the window would hide it with no way to get it back.
            MinimizeToTray = false;
        }

        if (_isLoading)
        {
            return;
        }

        _settings.Current.ShowTrayIcon = value;
        Save();
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _settings.Current.MinimizeToTray = value;
        Save();
    }

    partial void OnAutoGameModeChanged(bool value)
    {
        // Applied straight away rather than on next launch, so the effect of the switch is
        // something the user can see before they close the page.
        _automation.IsEnabled = value;

        if (_isLoading)
        {
            return;
        }

        _settings.Current.AutoGameMode = value;
        Save();
    }

    [RelayCommand]
    private async Task AddGameAsync()
    {
        OperationResult result = await _watcher
            .AddAsync(NewGameName, NewGameName)
            .ConfigureAwait(true);

        if (!result.Success)
        {
            _interaction.ShowError(result.Describe(_localization));
            return;
        }

        if (!result.Changed)
        {
            _interaction.ShowInfo(result.Describe(_localization));
            return;
        }

        NewGameName = string.Empty;
        RefreshWatchedGames();
    }

    [RelayCommand]
    private async Task RemoveGameAsync(WatchedGame? game)
    {
        if (game is null)
        {
            return;
        }

        await _watcher.RemoveAsync(game.ProcessName).ConfigureAwait(true);
        RefreshWatchedGames();
    }

    [RelayCommand]
    private async Task ToggleGameAsync(WatchedGame? game)
    {
        if (game is null)
        {
            return;
        }

        await _watcher.SetEnabledAsync(game.ProcessName, !game.Enabled).ConfigureAwait(true);
        RefreshWatchedGames();
    }

    private void RefreshWatchedGames()
    {
        WatchedGames.Clear();
        foreach (WatchedGame game in _watcher.Games.OrderBy(g => g.DisplayName, StringComparer.CurrentCulture))
        {
            WatchedGames.Add(game);
        }

        RefreshSummaries();
    }

    partial void OnVerboseLoggingChanged(bool value)
    {
        // Applied immediately rather than on next launch: the reason to turn it on is that
        // something is misbehaving right now.
        _diagnostics.IsVerbose = value;

        if (_isLoading)
        {
            return;
        }

        _settings.Current.VerboseLogging = value;
        Save();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Report(_shell.OpenFolder(_environment.DataDirectory));
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    /// <summary>
    /// Bundles the environment, the change journal and the log tail into one file, so a tester
    /// can send a single attachment instead of hunting through %ProgramData%.
    /// </summary>
    [RelayCommand]
    private async Task CreateReportAsync()
    {
        if (IsBuildingReport)
        {
            return;
        }

        IsBuildingReport = true;
        try
        {
            DiagnosticsReport report = await _diagnostics.WriteReportAsync().ConfigureAwait(true);

            if (!report.Result.Success)
            {
                _interaction.ShowError(report.Result.Describe(_localization));
                return;
            }

            LastReportPath = report.FilePath;
            _interaction.ShowSuccess(
                string.Format(
                    _localization["Settings_Report_Done"],
                    Path.GetFileName(report.FilePath),
                    report.LogLines,
                    report.JournalEntries));

            Reveal(report.FilePath);
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
        finally
        {
            IsBuildingReport = false;
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Report(_shell.OpenFolder(_diagnostics.LogDirectory));
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    /// <summary>
    /// Opens Explorer with the file already selected - one less step than opening the folder.
    /// A refusal is deliberately silent: the path is on screen either way.
    /// </summary>
    private void Reveal(string filePath) => _ = _shell.RevealFile(filePath);

    [RelayCommand]
    private void OpenRepository()
    {
        try
        {
            Report(_shell.OpenUrl(RepositoryUrl));
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    private void Save() => _ = _settings.SaveAsync();

    /// <summary>Surfaces a shell refusal. Silence here would look like a dead button.</summary>
    private void Report(OperationResult result)
    {
        if (!result.Success)
        {
            _interaction.ShowError(result.Describe(_localization));
        }
    }
}
