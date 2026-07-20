using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IRegistryService _registry;
    private readonly ISystemInfoService _systemInfo;
    private readonly IServiceManager _serviceManager;
    private readonly IProcessService _processService;
    private readonly IPowerService _powerService;
    private readonly DispatcherTimer _refreshTimer;

    [ObservableProperty] private string _cpuName = "Loading...";
    [ObservableProperty] private string _gpuName = "Loading...";
    [ObservableProperty] private string _ramInfo = "Loading...";
    [ObservableProperty] private string _osInfo = "Loading...";

    [ObservableProperty] private float _cpuUsage;
    [ObservableProperty] private long _ramUsedMb;
    [ObservableProperty] private long _ramTotalMb;
    [ObservableProperty] private int _runningProcesses;
    [ObservableProperty] private string _powerPlan = "Unknown";

    [ObservableProperty] private int _appliedTweaksCount;
    [ObservableProperty] private int _totalTweaksCount;
    [ObservableProperty] private int _stoppedServicesCount;
    [ObservableProperty] private int _totalServicesCount;
    [ObservableProperty] private double _optimizationScore;

    [ObservableProperty] private bool _isOptimizing;

    public ObservableCollection<float> CpuHistory { get; } = [];

    public DashboardViewModel(
        IRegistryService registry,
        ISystemInfoService systemInfo,
        IServiceManager serviceManager,
        IProcessService processService,
        IPowerService powerService)
    {
        _registry = registry;
        _systemInfo = systemInfo;
        _serviceManager = serviceManager;
        _processService = processService;
        _powerService = powerService;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshStats();
    }

    public void Initialize()
    {
        // Load hardware info
        var hw = _systemInfo.GetHardwareInfo();
        CpuName = hw.CpuName;
        GpuName = hw.GpuName;
        RamInfo = $"{hw.RamTotalMb / 1024} GB";
        OsInfo = $"Windows {hw.OsBuild}";

        // Count tweaks status
        TotalTweaksCount = GamingTweaks.All.Length;
        AppliedTweaksCount = GamingTweaks.All.Count(t =>
            GamingTweaks.CheckStatus(t, _registry) == TweakStatus.Applied);

        // Count services
        var services = _serviceManager.GetManagedServices();
        TotalServicesCount = services.Count;
        StoppedServicesCount = services.Count(s => !s.IsRunning);

        // Power plan
        PowerPlan = _powerService.IsUltimatePerformanceActive() ? "Ultimate Performance" : "Balanced";

        // Calculate score
        CalculateScore();

        // Start live monitoring
        RefreshStats();
        _refreshTimer.Start();
    }

    private void RefreshStats()
    {
        var snapshot = _systemInfo.GetCurrentSnapshot();
        CpuUsage = snapshot.CpuUsage;
        RamUsedMb = snapshot.RamUsedMb;
        RamTotalMb = snapshot.RamTotalMb;
        RunningProcesses = snapshot.RunningProcesses;

        CpuHistory.Add(snapshot.CpuUsage);
        if (CpuHistory.Count > 60) CpuHistory.RemoveAt(0);
    }

    private void CalculateScore()
    {
        double tweakScore = TotalTweaksCount > 0 ? (double)AppliedTweaksCount / TotalTweaksCount * 40 : 0;
        double serviceScore = TotalServicesCount > 0 ? (double)StoppedServicesCount / TotalServicesCount * 30 : 0;
        double powerScore = _powerService.IsUltimatePerformanceActive() ? 30 : 0;
        OptimizationScore = tweakScore + serviceScore + powerScore;
    }

    [RelayCommand]
    private async Task QuickOptimize()
    {
        IsOptimizing = true;
        await Task.Run(() =>
        {
            // Apply all safe gaming tweaks
            foreach (var tweak in GamingTweaks.All.Where(t => t.Risk == RiskLevel.Safe))
            {
                GamingTweaks.Apply(tweak, _registry);
            }

            // Stop safe services
            foreach (var svc in _serviceManager.GetManagedServices().Where(s => s.Risk == RiskLevel.Safe && s.IsRunning))
            {
                _serviceManager.StopService(svc.ServiceName);
                _serviceManager.SetServiceStartType(svc.ServiceName, disabled: true);
            }

            // Activate Ultimate Performance
            _powerService.ActivateUltimatePerformance();
            _powerService.DisableHibernation();

            // Clean RAM
            _processService.CleanStandbyMemory();
        });

        // Refresh counts
        AppliedTweaksCount = GamingTweaks.All.Count(t =>
            GamingTweaks.CheckStatus(t, _registry) == TweakStatus.Applied);
        var services = _serviceManager.GetManagedServices();
        StoppedServicesCount = services.Count(s => !s.IsRunning);
        PowerPlan = "Ultimate Performance";
        CalculateScore();
        IsOptimizing = false;
    }

    [RelayCommand]
    private async Task RestoreAll()
    {
        IsOptimizing = true;
        await Task.Run(() =>
        {
            foreach (var tweak in GamingTweaks.All)
            {
                GamingTweaks.Revert(tweak, _registry);
            }

            foreach (var svc in _serviceManager.GetManagedServices().Where(s => !s.IsRunning))
            {
                _serviceManager.SetServiceStartType(svc.ServiceName, disabled: false);
                _serviceManager.StartService(svc.ServiceName);
            }

            _powerService.RestoreBalancedPlan();
            _powerService.EnableHibernation();
        });

        AppliedTweaksCount = 0;
        StoppedServicesCount = 0;
        PowerPlan = "Balanced";
        CalculateScore();
        IsOptimizing = false;
    }

    [RelayCommand]
    private void CleanRam()
    {
        _processService.CleanStandbyMemory();
    }

    public void StopMonitoring()
    {
        _refreshTimer.Stop();
    }
}
