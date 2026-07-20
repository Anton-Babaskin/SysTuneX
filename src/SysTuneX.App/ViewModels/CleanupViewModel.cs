using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.Core.Services;

namespace SysTuneX.App.ViewModels;

public partial class CleanupViewModel : ObservableObject
{
    private readonly ICleanupService _cleanupService;

    [ObservableProperty] private string _cleanableSize = "Calculating...";
    [ObservableProperty] private string _lastCleanResult = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isScanning;

    public ObservableCollection<AppItem> RemovableApps { get; } = [];

    public CleanupViewModel(ICleanupService cleanupService)
    {
        _cleanupService = cleanupService;
    }

    public async void Initialize()
    {
        IsScanning = true;

        await Task.Run(() =>
        {
            var size = _cleanupService.GetTotalCleanableSize();
            CleanableSize = $"{size / (1024.0 * 1024.0):F1} MB";
        });

        // Load removable apps in background
        var apps = await Task.Run(() => _cleanupService.GetRemovableApps());
        RemovableApps.Clear();
        foreach (var app in apps)
            RemovableApps.Add(new AppItem { PackageName = app, DisplayName = app.Split('.').Last() });

        IsScanning = false;
    }

    [RelayCommand]
    private async Task CleanTemp()
    {
        IsBusy = true;
        long total = 0;
        await Task.Run(() =>
        {
            total += _cleanupService.CleanTempFiles();
            total += _cleanupService.CleanWindowsTemp();
            total += _cleanupService.CleanPrefetch();
        });
        LastCleanResult = $"Cleaned {total / (1024.0 * 1024.0):F1} MB";

        var newSize = await Task.Run(() => _cleanupService.GetTotalCleanableSize());
        CleanableSize = $"{newSize / (1024.0 * 1024.0):F1} MB";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task RemoveApp(AppItem app)
    {
        app.IsRemoving = true;
        var success = await Task.Run(() => _cleanupService.RemoveApp(app.PackageName));
        if (success)
            RemovableApps.Remove(app);
        else
            app.IsRemoving = false;
    }
}

public partial class AppItem : ObservableObject
{
    public string PackageName { get; init; } = "";
    public string DisplayName { get; init; } = "";

    [ObservableProperty]
    private bool _isRemoving;
}
