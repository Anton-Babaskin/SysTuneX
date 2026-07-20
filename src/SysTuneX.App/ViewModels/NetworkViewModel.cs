using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.ViewModels;

public partial class NetworkViewModel : ObservableObject
{
    private readonly IRegistryService _registry;
    private readonly INetworkService _networkService;

    public ObservableCollection<TweakItem> Tweaks { get; } = [];

    [ObservableProperty] private string _currentDnsPrimary = "Auto";
    [ObservableProperty] private string _currentDnsSecondary = "Auto";
    [ObservableProperty] private int _selectedDnsPreset = -1;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _appliedCount;

    public List<string> DnsPresetNames { get; } = NetworkTweaks.DnsPresets.Select(d => d.Name).ToList();

    public NetworkViewModel(IRegistryService registry, INetworkService networkService)
    {
        _registry = registry;
        _networkService = networkService;
    }

    public void Initialize()
    {
        Tweaks.Clear();
        foreach (var def in NetworkTweaks.All)
        {
            var item = NetworkTweaks.ToTweakItem(def);
            item.Status = NetworkTweaks.CheckStatus(def, _registry);
            item.IsEnabled = item.Status == TweakStatus.Applied;
            Tweaks.Add(item);
        }
        var dns = _networkService.GetCurrentDns();
        CurrentDnsPrimary = dns.Primary;
        CurrentDnsSecondary = dns.Secondary;
        AppliedCount = Tweaks.Count(t => t.Status == TweakStatus.Applied);
    }

    [RelayCommand]
    private async Task ToggleTweak(TweakItem item)
    {
        var def = NetworkTweaks.All.FirstOrDefault(t => t.Id == item.Id);
        if (def == null) return;

        item.IsBusy = true;
        await Task.Run(() =>
        {
            if (item.IsEnabled)
                NetworkTweaks.Revert(def, _registry);
            else
                NetworkTweaks.Apply(def, _registry);
        });
        item.Status = NetworkTweaks.CheckStatus(def, _registry);
        item.IsEnabled = item.Status == TweakStatus.Applied;
        item.IsBusy = false;
        AppliedCount = Tweaks.Count(t => t.Status == TweakStatus.Applied);
    }

    [RelayCommand]
    private async Task ApplyDns()
    {
        if (SelectedDnsPreset < 0 || SelectedDnsPreset >= NetworkTweaks.DnsPresets.Length)
            return;

        IsBusy = true;
        var preset = NetworkTweaks.DnsPresets[SelectedDnsPreset];
        await Task.Run(() => _networkService.SetDns(preset.Primary, preset.Secondary));
        CurrentDnsPrimary = preset.Primary;
        CurrentDnsSecondary = preset.Secondary;
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ResetDns()
    {
        IsBusy = true;
        await Task.Run(() => _networkService.ResetDns());
        var dns = _networkService.GetCurrentDns();
        CurrentDnsPrimary = dns.Primary;
        CurrentDnsSecondary = dns.Secondary;
        IsBusy = false;
    }
}
