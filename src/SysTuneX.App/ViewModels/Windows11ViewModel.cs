using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.ViewModels;

public partial class Windows11ViewModel : ObservableObject
{
    private readonly IRegistryService _registry;
    private readonly IRestorePointService _restorePoint;

    public ObservableCollection<TweakGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _requiresRestartVisible;
    [ObservableProperty] private int _appliedCount;
    [ObservableProperty] private int _totalCount;

    // VBS/HVCI status — highlighted prominently
    [ObservableProperty] private TweakStatus _vbsStatus = TweakStatus.Unknown;
    [ObservableProperty] private TweakStatus _hvciStatus = TweakStatus.Unknown;

    public Windows11ViewModel(IRegistryService registry, IRestorePointService restorePoint)
    {
        _registry = registry;
        _restorePoint = restorePoint;
    }

    public void Initialize()
    {
        Groups.Clear();

        var groupDefs = new[]
        {
            ("⚠️  Security Overhead (VBS / HVCI)", Windows11Tweaks.SecurityTweaks.AsEnumerable()),
            ("🤖  AI Features",                     Windows11Tweaks.AiTweaks.AsEnumerable()),
            ("🔍  Search & Bing",                   Windows11Tweaks.SearchTweaks.AsEnumerable()),
            ("📱  Widgets & Phone Link",             Windows11Tweaks.WidgetsTweaks.AsEnumerable()),
            ("⚡  System Responsiveness",            Windows11Tweaks.ResponsivenessTweaks.AsEnumerable()),
        };

        foreach (var (groupName, defs) in groupDefs)
        {
            var group = new TweakGroupViewModel { GroupName = groupName };
            foreach (var def in defs)
            {
                var item = Windows11Tweaks.ToTweakItem(def);
                item.Status = Windows11Tweaks.CheckStatus(def, _registry);
                item.IsEnabled = item.Status == TweakStatus.Applied;
                group.Tweaks.Add(item);
            }
            Groups.Add(group);
        }

        RefreshCounts();
        RefreshVbsStatus();
    }

    [RelayCommand]
    private async Task ToggleTweak(TweakItem item)
    {
        var def = Windows11Tweaks.All.FirstOrDefault(t => t.Id == item.Id);
        if (def == null) return;

        item.IsBusy = true;
        await Task.Run(() =>
        {
            if (item.IsEnabled)
                Windows11Tweaks.Apply(def, _registry);
            else
                Windows11Tweaks.Revert(def, _registry);
        });

        item.Status = Windows11Tweaks.CheckStatus(def, _registry);
        item.IsEnabled = item.Status == TweakStatus.Applied;
        item.IsBusy = false;

        RefreshCounts();
        RefreshVbsStatus();
    }

    [RelayCommand]
    private async Task ApplyAll()
    {
        IsBusy = true;
        await _restorePoint.CreateAsync("SysTuneX — before Win11 Apply All");
        await Task.Run(() =>
        {
            foreach (var def in Windows11Tweaks.All)
                Windows11Tweaks.Apply(def, _registry);
        });
        Initialize();
        RequiresRestartVisible = true;
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ApplyAiOnly()
    {
        IsBusy = true;
        await Task.Run(() =>
        {
            foreach (var def in Windows11Tweaks.AiTweaks
                .Concat(Windows11Tweaks.SearchTweaks)
                .Concat(Windows11Tweaks.WidgetsTweaks)
                .Concat(Windows11Tweaks.ResponsivenessTweaks))
            {
                Windows11Tweaks.Apply(def, _registry);
            }
        });
        Initialize();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ApplyVbs()
    {
        IsBusy = true;
        await _restorePoint.CreateAsync("SysTuneX — before VBS/HVCI disable");
        await Task.Run(() =>
        {
            foreach (var def in Windows11Tweaks.SecurityTweaks)
                Windows11Tweaks.Apply(def, _registry);
        });
        Initialize();
        RequiresRestartVisible = true;
        IsBusy = false;
    }

    [RelayCommand]
    private async Task RevertAll()
    {
        IsBusy = true;
        await Task.Run(() =>
        {
            foreach (var def in Windows11Tweaks.All)
                Windows11Tweaks.Revert(def, _registry);
        });
        Initialize();
        RequiresRestartVisible = false;
        IsBusy = false;
    }

    private void RefreshCounts()
    {
        TotalCount = Windows11Tweaks.All.Count();
        AppliedCount = Windows11Tweaks.All.Count(d =>
            Windows11Tweaks.CheckStatus(d, _registry) == TweakStatus.Applied);
    }

    private void RefreshVbsStatus()
    {
        var vbsDef = Windows11Tweaks.SecurityTweaks[0];
        var hvciDef = Windows11Tweaks.SecurityTweaks[1];
        VbsStatus = Windows11Tweaks.CheckStatus(vbsDef, _registry);
        HvciStatus = Windows11Tweaks.CheckStatus(hvciDef, _registry);
    }
}

public partial class TweakGroupViewModel : ObservableObject
{
    public string GroupName { get; init; } = "";
    public ObservableCollection<TweakItem> Tweaks { get; } = [];
}
