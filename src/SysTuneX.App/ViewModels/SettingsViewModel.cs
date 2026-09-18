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

/// <summary>
/// Eight folded cards. Five of them are their own view models; what is left here is the two cards
/// that are always open - how the application looks and what it asks before writing - plus the
/// tray, the "about" block, and the loading flag the sections share.
///
/// It was all one class: fourteen dependencies and every field, handler and summary property of
/// all eight cards in a single stripe, seven hundred lines long.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel, ISettingsHost
{
    private const string RepositoryUrl = "https://github.com/Anton-Babaskin/SysTuneX";

    private readonly IAppSettingsService _settings;
    private readonly ILocalizationService _localization;
    private readonly IEnvironmentService _environment;
    private readonly IUserInteraction _interaction;
    private readonly ITrayIconService _tray;
    private readonly IShellLauncher _shell;
    private readonly IWindowAppearance _appearance;

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
    [NotifyPropertyChangedFor(nameof(CanMinimizeToTray))]
    [NotifyPropertyChangedFor(nameof(TraySummary))]
    private bool _showTrayIcon = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TraySummary))]
    private bool _minimizeToTray;

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
        IWindowAppearance appearance)
    {
        _settings = settings;
        _localization = localization;
        _environment = environment;
        _interaction = interaction;
        _tray = tray;
        _shell = shell;
        _appearance = appearance;

        Games = new GameWatchSettingsViewModel(this, watcher, automation, interaction, localization);
        Schedule = new ScheduleSettingsViewModel(this, scheduler, localization);
        Power = new PowerSchemeSettingsViewModel(power, interaction, localization);
        Compact = new CompactMonitorSettingsViewModel(this, compact, localization);
        Diagnostics = new DiagnosticsSettingsViewModel(this, diagnostics, shell, interaction, localization);

        Languages = localization.AvailableLanguages;
    }

    public GameWatchSettingsViewModel Games { get; }

    public ScheduleSettingsViewModel Schedule { get; }

    public PowerSchemeSettingsViewModel Power { get; }

    public CompactMonitorSettingsViewModel Compact { get; }

    public DiagnosticsSettingsViewModel Diagnostics { get; }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";

    public string DataDirectory => _environment.DataDirectory;

    public string WindowsDescription => _environment.Windows.ToString();

    public string ElevationDescription =>
        _environment.IsElevated ? _localization["Dashboard_Elevated"] : _localization["Banner_NotElevated_Title"];

    /// <summary>Closing to the tray only makes sense while there is a tray icon to come back from.</summary>
    public bool CanMinimizeToTray => ShowTrayIcon;

    public string TraySummary => !ShowTrayIcon
        ? _localization["Settings_Summary_Off"]
        : MinimizeToTray
            ? _localization["Settings_Summary_TrayAndClose"]
            : _localization["Settings_Summary_On"];

    AppSettings ISettingsHost.Settings => _settings.Current;

    bool ISettingsHost.IsLoading => _isLoading;

    void ISettingsHost.Save() => Save();

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
        ShowTrayIcon = current.ShowTrayIcon;
        MinimizeToTray = current.MinimizeToTray;

        Games.Load();
        Schedule.Load();
        Compact.Load();
        Diagnostics.Load();

        _isLoading = false;

        return Power.LoadAsync();
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

        // Every folded card shows what it is set to, in words, so all of them have to be re-read.
        OnPropertyChanged(nameof(ElevationDescription));
        OnPropertyChanged(nameof(TraySummary));

        Games.RefreshText();
        Schedule.RefreshText();
        Power.RefreshText();
        Compact.RefreshText();
        Diagnostics.RefreshText();

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

    [RelayCommand]
    private void OpenDataFolder() => Report(() => _shell.OpenFolder(_environment.DataDirectory));

    [RelayCommand]
    private void OpenRepository() => Report(() => _shell.OpenUrl(RepositoryUrl));

    private void Save() => _ = _settings.SaveAsync();

    /// <summary>Surfaces a shell refusal. Silence here would look like a dead button.</summary>
    private void Report(Func<OperationResult> action)
    {
        try
        {
            OperationResult result = action();

            if (!result.Success)
            {
                _interaction.ShowError(result.Describe(_localization));
            }
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }
}
