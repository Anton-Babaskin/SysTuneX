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

    public SettingsViewModel(
        IAppSettingsService settings,
        ILocalizationService localization,
        IEnvironmentService environment,
        IUserInteraction interaction)
    {
        _settings = settings;
        _localization = localization;
        _environment = environment;
        _interaction = interaction;

        Languages = localization.AvailableLanguages;
    }

    public IReadOnlyList<LanguageOption> Languages { get; }

    public string Version => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";

    public string DataDirectory => _environment.DataDirectory;

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
