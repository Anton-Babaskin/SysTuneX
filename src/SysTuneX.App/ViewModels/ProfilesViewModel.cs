using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly IRegistryService _registry;
    private readonly IServiceManager _serviceManager;
    private readonly IPowerService _powerService;
    private readonly IProcessService _processService;

    public ObservableCollection<GameProfile> Profiles { get; } = [];

    [ObservableProperty] private GameProfile? _selectedProfile;
    [ObservableProperty] private bool _isApplying;
    [ObservableProperty] private string _statusMessage = "";

    public ProfilesViewModel(
        IRegistryService registry,
        IServiceManager serviceManager,
        IPowerService powerService,
        IProcessService processService)
    {
        _registry = registry;
        _serviceManager = serviceManager;
        _powerService = powerService;
        _processService = processService;
    }

    public void Initialize()
    {
        Profiles.Clear();
        foreach (var profile in GameProfiles.BuiltIn)
            Profiles.Add(profile);
    }

    [RelayCommand]
    private async Task ApplyProfile(GameProfile profile)
    {
        IsApplying = true;
        SelectedProfile = profile;
        StatusMessage = $"Applying {profile.Name}...";

        await Task.Run(() =>
        {
            // Apply gaming tweaks from profile
            foreach (var tweakId in profile.TweakIds)
            {
                var gamingDef = GamingTweaks.All.FirstOrDefault(t => t.Id == tweakId);
                if (gamingDef != null)
                {
                    GamingTweaks.Apply(gamingDef, _registry);
                    continue;
                }

                var netDef = NetworkTweaks.All.FirstOrDefault(t => t.Id == tweakId);
                if (netDef != null)
                {
                    NetworkTweaks.Apply(netDef, _registry);
                }
            }

            // Stop services from profile
            foreach (var svcName in profile.ServiceNames)
            {
                _serviceManager.StopService(svcName);
                _serviceManager.SetServiceStartType(svcName, disabled: true);
            }

            // Activate Ultimate Performance
            _powerService.ActivateUltimatePerformance();
            _powerService.DisableHibernation();

            // Clean RAM
            _processService.CleanStandbyMemory();
        });

        StatusMessage = $"{profile.Name} applied successfully!";
        IsApplying = false;
    }

    [RelayCommand]
    private async Task ResetProfile()
    {
        IsApplying = true;
        StatusMessage = "Restoring defaults...";

        await Task.Run(() =>
        {
            foreach (var def in GamingTweaks.All)
                GamingTweaks.Revert(def, _registry);

            foreach (var def in NetworkTweaks.All)
                NetworkTweaks.Revert(def, _registry);

            foreach (var svc in _serviceManager.GetManagedServices().Where(s => !s.IsRunning))
            {
                _serviceManager.SetServiceStartType(svc.ServiceName, disabled: false);
                _serviceManager.StartService(svc.ServiceName);
            }

            _powerService.RestoreBalancedPlan();
        });

        StatusMessage = "All settings restored to defaults";
        SelectedProfile = null;
        IsApplying = false;
    }
}
