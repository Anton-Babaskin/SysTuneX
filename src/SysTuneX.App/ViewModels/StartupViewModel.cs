using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;

namespace SysTuneX.App.ViewModels;

public partial class StartupViewModel : ObservableObject
{
    private readonly IStartupService _startupService;

    public ObservableCollection<StartupItem> Items { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _enabledCount;
    [ObservableProperty] private int _totalCount;

    public StartupViewModel(IStartupService startupService)
    {
        _startupService = startupService;
    }

    public void Initialize()
    {
        Items.Clear();
        foreach (var item in _startupService.GetStartupItems())
            Items.Add(item);
        RefreshCounts();
    }

    [RelayCommand]
    private async Task ToggleItem(StartupItem item)
    {
        item.IsBusy = true;
        var desired = item.IsEnabled;
        bool success = await Task.Run(() => _startupService.SetEnabled(item, desired));
        if (!success)
            item.IsEnabled = !desired; // revert UI on failure
        item.IsBusy = false;
        RefreshCounts();
    }

    private void RefreshCounts()
    {
        TotalCount = Items.Count;
        EnabledCount = Items.Count(i => i.IsEnabled);
    }
}
