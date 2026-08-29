using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly IPowerService _power;
    private readonly IGameWatcher _watcher;
    private readonly GameModeAutomation _automation;
    private readonly ITrayIconService _tray;
    private readonly GameModeScheduler _scheduler;

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

    public SettingsViewModel(
        IAppSettingsService settings,
        ILocalizationService localization,
        IEnvironmentService environment,
        IUserInteraction interaction,
        IDiagnosticsService diagnostics,
        IPowerService power,
        IGameWatcher watcher,
        GameModeAutomation automation,
        ITrayIconService tray,
        GameModeScheduler scheduler)
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

        System.Windows.Window? mainWindow = System.Windows.Application.Current.MainWindow;

        if (theme == ApplicationTheme.Unknown)
        {
            ApplicationThemeManager.ApplySystemTheme();

            if (mainWindow is not null)
            {
                SystemThemeWatcher.Watch(mainWindow);
            }
        }
        else
        {
            // Stop following Windows before applying the choice. The watcher used to be left
            // running, so picking Dark by hand worked until the next time Windows changed its own
            // theme - at which point the watcher applied that over the top and the manual setting
            // silently stopped holding.
            if (mainWindow is not null)
            {
                SystemThemeWatcher.UnWatch(mainWindow);
            }

            ApplicationThemeManager.Apply(theme);
        }

        Save();
    }

    partial void OnSelectedBackdropChanged(string value)
    {
        if (_isLoading || !Enum.TryParse(value, out WindowBackdropType backdrop))
        {
            return;
        }

        _settings.Current.Backdrop = backdrop;

        if (System.Windows.Application.Current.MainWindow is FluentWindow window)
        {
            window.WindowBackdropType = backdrop;
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
            Directory.CreateDirectory(_environment.DataDirectory);
            Process.Start(new ProcessStartInfo { FileName = _environment.DataDirectory, UseShellExecute = true });
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
            Directory.CreateDirectory(_diagnostics.LogDirectory);
            Process.Start(new ProcessStartInfo { FileName = _diagnostics.LogDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    /// <summary>Opens Explorer with the file already selected - one less step than opening the folder.</summary>
    private static void Reveal(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true,
            });
        }
        catch
        {
            // Not being able to show the folder is not worth an error toast; the path is on screen.
        }
    }

    [RelayCommand]
    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = RepositoryUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    private void Save() => _ = _settings.SaveAsync();
}
