using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;

namespace SysTuneX.App.ViewModels;

public partial class ServicesViewModel : ObservableObject
{
    private readonly IServiceManager _serviceManager;

    public ObservableCollection<ServiceItem> Services { get; } = [];

    [ObservableProperty] private int _runningCount;
    [ObservableProperty] private int _stoppedCount;
    [ObservableProperty] private bool _isBusy;

    public ServicesViewModel(IServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }

    public void Initialize()
    {
        Services.Clear();
        foreach (var svc in _serviceManager.GetManagedServices())
            Services.Add(svc);
        UpdateCounts();
    }

    [RelayCommand]
    private async Task ToggleService(ServiceItem item)
    {
        item.IsBusy = true;
        await Task.Run(() =>
        {
            if (item.IsRunning)
            {
                _serviceManager.StopService(item.ServiceName);
                _serviceManager.SetServiceStartType(item.ServiceName, disabled: true);
            }
            else
            {
                _serviceManager.SetServiceStartType(item.ServiceName, disabled: false);
                _serviceManager.StartService(item.ServiceName);
            }
        });
        item.IsRunning = _serviceManager.IsServiceRunning(item.ServiceName);
        item.IsBusy = false;
        UpdateCounts();
    }

    [RelayCommand]
    private async Task StopAll()
    {
        IsBusy = true;
        await Task.Run(() =>
        {
            foreach (var svc in Services.Where(s => s.IsRunning))
            {
                _serviceManager.StopService(svc.ServiceName);
                _serviceManager.SetServiceStartType(svc.ServiceName, disabled: true);
            }
        });
        Initialize();
        IsBusy = false;
    }

    [RelayCommand]
    private async Task StartAll()
    {
        IsBusy = true;
        await Task.Run(() =>
        {
            foreach (var svc in Services.Where(s => !s.IsRunning))
            {
                _serviceManager.SetServiceStartType(svc.ServiceName, disabled: false);
                _serviceManager.StartService(svc.ServiceName);
            }
        });
        Initialize();
        IsBusy = false;
    }

    private void UpdateCounts()
    {
        RunningCount = Services.Count(s => s.IsRunning);
        StoppedCount = Services.Count(s => !s.IsRunning);
    }
}
