using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Converters;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

public sealed partial class CleanupViewModel : PageViewModel
{
    private readonly ICleanupService _cleanup;
    private readonly IUserInteraction _interaction;
    private readonly ILocalizationService _localization;
    private readonly CatalogText _text;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isLoadingApps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSizeText))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private long _selectedBytes;

    [ObservableProperty]
    private long _totalBytes;

    public CleanupViewModel(
        ICleanupService cleanup,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text)
    {
        _cleanup = cleanup;
        _interaction = interaction;
        _localization = localization;
        _text = text;

        localization.LanguageChanged += (_, _) => RefreshText();
    }

    public ObservableCollection<CleanupTargetViewModel> Targets { get; } = [];

    public ObservableCollection<AppPackageViewModel> Apps { get; } = [];

    public string SelectedSizeText => BytesToSizeConverter.Format(SelectedBytes);

    public string TotalSizeText => BytesToSizeConverter.Format(TotalBytes);

    public bool HasSelection => SelectedBytes > 0 || Targets.Any(t => t.IsSelected);

    protected override async Task OnEnterAsync()
    {
        if (Targets.Count == 0)
        {
            foreach (CleanupTarget target in _cleanup.GetTargets())
            {
                var item = new CleanupTargetViewModel(target, _text);
                item.PropertyChanged += OnTargetChanged;
                Targets.Add(item);
            }
        }

        await ScanAsync().ConfigureAwait(true);
        _ = LoadAppsAsync();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;

        try
        {
            foreach (CleanupTargetViewModel target in Targets)
            {
                target.IsScanning = true;
            }

            foreach (CleanupTargetViewModel target in Targets)
            {
                try
                {
                    CleanupScanResult result = await _cleanup.ScanAsync(target.Target, PageToken).ConfigureAwait(true);
                    target.Apply(result);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                finally
                {
                    target.IsScanning = false;
                }
            }

            UpdateTotals();
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        List<CleanupTargetViewModel> selected = Targets.Where(t => t.IsSelected && t.SizeBytes > 0).ToList();

        if (selected.Count == 0)
        {
            _interaction.ShowInfo(_localization["Msg_NoChanges"]);
            return;
        }

        long bytes = selected.Sum(t => t.SizeBytes);
        int files = selected.Sum(t => t.FileCount);

        // The README asks for cleanup to state exactly what it will remove before it runs.
        bool confirmed = await _interaction
            .ConfirmAsync(
                _localization["Dialog_Cleanup_Title"],
                _localization.Format("Dialog_Cleanup_Message", BytesToSizeConverter.Format(bytes), files) +
                Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, selected.Select(t => $"• {t.Name} — {t.SizeText}")),
                _localization["Cleanup_Clean"],
                PageToken)
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunBusyAsync(
            _localization["Common_Working"],
            async token =>
            {
                var progress = new Progress<CleanupProgress>(p =>
                {
                    BusyMessage = p.TargetName;
                    Progress = p.TotalTargets == 0 ? -1 : p.CompletedTargets * 100.0 / p.TotalTargets;
                });

                CleanupRunResult result = await _cleanup
                    .CleanAsync(selected.Select(t => t.Target), progress, token)
                    .ConfigureAwait(true);

                await ScanAsync().ConfigureAwait(true);

                _interaction.ShowSuccess(
                    _localization.Format("Msg_CleanupDone", BytesToSizeConverter.Format(result.FreedBytes)));

                if (result.Errors.Count > 0)
                {
                    _interaction.ShowWarning(result.Errors[0]);
                }
            }).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RemoveAppAsync(AppPackageViewModel? app)
    {
        if (app is null || app.IsBusy)
        {
            return;
        }

        string message = _localization["Dialog_RemoveApp_Message"];

        if (app.IsSystemRelevant)
        {
            message += Environment.NewLine + Environment.NewLine + _localization["Dialog_RemoveApp_SystemRelevant"];
        }

        bool confirmed = await _interaction
            .ConfirmAsync(
                _localization.Format("Dialog_RemoveApp_Title", app.DisplayName),
                message,
                _localization["Cleanup_Remove"],
                PageToken)
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        app.IsBusy = true;

        try
        {
            OperationResult result = await _cleanup
                .RemoveAppAsync(app.PackageName, PageToken)
                .ConfigureAwait(true);

            if (result.Success)
            {
                Apps.Remove(app);
                _interaction.ShowSuccess(_localization.Format("Msg_AppRemoved", app.DisplayName));
            }
            else
            {
                _interaction.ShowError(result.Describe(_localization), app.DisplayName);
            }
        }
        finally
        {
            app.IsBusy = false;
        }
    }

    private async Task LoadAppsAsync()
    {
        IsLoadingApps = true;

        try
        {
            IReadOnlyList<AppPackage> packages = await _cleanup
                .GetRemovableAppsAsync(PageToken)
                .ConfigureAwait(true);

            Apps.Clear();

            foreach (AppPackage package in packages)
            {
                Apps.Add(new AppPackageViewModel(package));
            }
        }
        catch (OperationCanceledException)
        {
            // Page left while PowerShell was still enumerating.
        }
        finally
        {
            IsLoadingApps = false;
        }
    }

    private void OnTargetChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupTargetViewModel.IsSelected) or nameof(CleanupTargetViewModel.SizeBytes))
        {
            UpdateTotals();
        }
    }

    private void UpdateTotals()
    {
        TotalBytes = Targets.Sum(t => t.SizeBytes);
        SelectedBytes = Targets.Where(t => t.IsSelected).Sum(t => t.SizeBytes);
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void RefreshText()
    {
        foreach (CleanupTargetViewModel target in Targets)
        {
            target.RefreshText();
        }
    }
}

public sealed partial class CleanupTargetViewModel : ObservableObject
{
    private readonly CatalogText _text;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isScanning = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    private long _sizeBytes;

    [ObservableProperty]
    private int _fileCount;

    [ObservableProperty]
    private string _paths = string.Empty;

    public CleanupTargetViewModel(CleanupTarget target, CatalogText text)
    {
        Target = target;
        _text = text;
        _isSelected = target.EnabledByDefault;
    }

    public CleanupTarget Target { get; }

    public string Id => Target.Id;

    public string Name => _text.Name(Target);

    public string Description => _text.Description(Target);

    public RiskLevel Risk => Target.Risk;

    public string RiskText => _text.Risk(Target.Risk);

    public string SizeText => BytesToSizeConverter.Format(SizeBytes);

    public bool HasContent => SizeBytes > 0;

    public void Apply(CleanupScanResult result)
    {
        SizeBytes = result.SizeBytes;
        FileCount = result.FileCount;
        Paths = string.Join(Environment.NewLine, result.ResolvedPaths);
    }

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(RiskText));
    }
}

public sealed partial class AppPackageViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    public AppPackageViewModel(AppPackage package) => Package = package;

    public AppPackage Package { get; }

    public string PackageName => Package.PackageFamilyName;

    public string DisplayName => Package.DisplayName;

    public bool IsSystemRelevant => Package.IsSystemRelevant;
}
