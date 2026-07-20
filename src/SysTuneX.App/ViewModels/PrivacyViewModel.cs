using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.ViewModels;

public partial class PrivacyViewModel : ObservableObject
{
    private readonly IRegistryService _registry;
    private readonly IPrivacyService _privacyService;

    public ObservableCollection<TweakItem> Tweaks { get; } = [];

    [ObservableProperty] private bool _hostsBlocked;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _appliedCount;

    public PrivacyViewModel(IRegistryService registry, IPrivacyService privacyService)
    {
        _registry = registry;
        _privacyService = privacyService;
    }

    public void Initialize()
    {
        Tweaks.Clear();
        foreach (var def in PrivacyTweaks.All)
        {
            var item = PrivacyTweaks.ToTweakItem(def);
            item.Status = PrivacyTweaks.CheckStatus(def, _registry);
            item.IsEnabled = item.Status == TweakStatus.Applied;
            Tweaks.Add(item);
        }
        HostsBlocked = _privacyService.AreHostsBlocked();
        AppliedCount = Tweaks.Count(t => t.Status == TweakStatus.Applied);
    }

    [RelayCommand]
    private async Task ToggleTweak(TweakItem item)
    {
        var def = PrivacyTweaks.All.FirstOrDefault(t => t.Id == item.Id);
        if (def == null) return;

        item.IsBusy = true;
        await Task.Run(() =>
        {
            if (item.IsEnabled)
                PrivacyTweaks.Revert(def, _registry);
            else
                PrivacyTweaks.Apply(def, _registry);
        });
        item.Status = PrivacyTweaks.CheckStatus(def, _registry);
        item.IsEnabled = item.Status == TweakStatus.Applied;
        item.IsBusy = false;
        AppliedCount = Tweaks.Count(t => t.Status == TweakStatus.Applied);
    }

    [RelayCommand]
    private async Task ToggleHostsBlock()
    {
        IsBusy = true;
        await Task.Run(() =>
        {
            if (HostsBlocked)
                _privacyService.UnblockTelemetryHosts();
            else
                _privacyService.BlockTelemetryHosts();
        });
        HostsBlocked = _privacyService.AreHostsBlocked();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ApplyAll()
    {
        IsBusy = true;
        await Task.Run(() =>
        {
            foreach (var def in PrivacyTweaks.All)
                PrivacyTweaks.Apply(def, _registry);
            _privacyService.BlockTelemetryHosts();
        });
        Initialize();
        IsBusy = false;
    }
}
