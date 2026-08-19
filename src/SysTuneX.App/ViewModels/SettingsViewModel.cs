using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
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
    private bool _verboseLogging;

    [ObservableProperty]
    private string? _lastReportPath;

    [ObservableProperty]
    private bool _isBuildingReport;

    public SettingsViewModel(
        IAppSettingsService settings,
        ILocalizationService localization,
        IEnvironmentService environment,
        IUserInteraction interaction,
        IDiagnosticsService diagnostics)
    {
        _settings = settings;
        _localization = localization;
        _environment = environment;
        _interaction = interaction;
        _diagnostics = diagnostics;

        Languages = localization.AvailableLanguages;
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

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

        _isLoading = false;
        return Task.CompletedTask;
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

        if (theme == ApplicationTheme.Unknown)
        {
            ApplicationThemeManager.ApplySystemTheme();

            if (System.Windows.Application.Current.MainWindow is { } window)
            {
                SystemThemeWatcher.Watch(window);
            }
        }
        else
        {
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
                _interaction.ShowError(report.Result.Message ?? _localization["Msg_Error"]);
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
