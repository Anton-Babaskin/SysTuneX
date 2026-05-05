using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.ViewModels;

public partial class GamingViewModel : ObservableObject
{
    private readonly IRegistryService _registry;
    private readonly IRestorePointService _restorePoint;

    public ObservableCollection<TweakItem> Tweaks { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _appliedCount;
    [ObservableProperty] private int _totalCount;

    public GamingViewModel(IRegistryService registry, IRestorePointService restorePoint)
    {
        _registry = registry;
        _restorePoint = restorePoint;
    }

    public void Initialize()
    {
        Tweaks.Clear();
        foreach (var def in GamingTweaks.All)
        {
            var item = GamingTweaks.ToTweakItem(def);
            item.Status = GamingTweaks.CheckStatus(def, _registry);
            item.IsEnabled = item.Status == TweakStatus.Applied;
            Tweaks.Add(item);
        }
        TotalCount = Tweaks.Count;
        AppliedCount = Tweaks.Count(t => t.Status == TweakStatus.Applied);
    }

    [RelayCommand]
    private async Task ToggleTweak(TweakItem item)
    {
        var def = GamingTweaks.All.FirstOrDefault(t => t.Id == item.Id);
        if (def == null) return;

        item.IsBusy = true;
        await Task.Run(() =>
        {
            if (item.IsEnabled)
                GamingTweaks.Apply(def, _registry);
            else
                GamingTweaks.Revert(def, _registry);
        });

        item.Status = GamingTweaks.CheckStatus(def, _registry);
        item.IsEnabled = item.Status == TweakStatus.Applied;
        item.IsBusy = false;
        AppliedCount = Tweaks.Count(t => t.Status == TweakStatus.Applied);
    }

    [RelayCommand]
    private async Task ApplyAll()
    {
        IsBusy = true;
        await _restorePoint.CreateAsync("SysTuneX — before Gaming Apply All");
        await Task.Run(() =>
        {
            foreach (var def in GamingTweaks.All)
                GamingTweaks.Apply(def, _registry);
        });
        Initialize();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task RevertAll()
    {
        IsBusy = true;
        await Task.Run(() =>
        {
            foreach (var def in GamingTweaks.All)
                GamingTweaks.Revert(def, _registry);
        });
        Initialize();
        IsBusy = false;
    }
}
