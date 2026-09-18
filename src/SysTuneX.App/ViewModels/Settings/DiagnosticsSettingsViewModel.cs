using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// Logging, and the one file a tester can attach to a bug report.
/// </summary>
public sealed partial class DiagnosticsSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _host;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IShellLauncher _shell;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DiagnosticsSummary))]
    private bool _verboseLogging;

    [ObservableProperty]
    private string? _lastReportPath;

    [ObservableProperty]
    private bool _isBuildingReport;

    public DiagnosticsSettingsViewModel(
        ISettingsHost host,
        IDiagnosticsService diagnostics,
        IShellLauncher shell,
        IUserInteraction interaction,
        ILocalizationService localization)
    {
        _host = host;
        _diagnostics = diagnostics;
        _shell = shell;
        _interaction = interaction;
        _localization = localization;
    }

    public string LogDirectory => _diagnostics.LogDirectory;

    public string DiagnosticsSummary => VerboseLogging
        ? _localization["Settings_Summary_VerboseOn"]
        : _localization["Settings_Summary_VerboseOff"];

    public void Load() => VerboseLogging = _host.Settings.VerboseLogging;

    partial void OnVerboseLoggingChanged(bool value)
    {
        // Applied immediately rather than on next launch: the reason to turn it on is that
        // something is misbehaving right now.
        _diagnostics.IsVerbose = value;

        if (_host.IsLoading)
        {
            return;
        }

        _host.Settings.VerboseLogging = value;
        _host.Save();
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

            // Opens Explorer with the file already selected - one less step than opening the
            // folder. A refusal is deliberately silent: the path is on screen either way.
            _ = _shell.RevealFile(report.FilePath);
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
            OperationResult opened = _shell.OpenFolder(_diagnostics.LogDirectory);

            // Silence here would look like a dead button.
            if (!opened.Success)
            {
                _interaction.ShowError(opened.Describe(_localization));
            }
        }
        catch (Exception ex)
        {
            _interaction.ShowError(ex.Message);
        }
    }

    /// <summary>Re-reads the words after a language change; the values themselves do not move.</summary>
    public void RefreshText() => OnPropertyChanged(nameof(DiagnosticsSummary));
}
