using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// The numbers that move: processor and memory load, the drive, uptime, and the temperatures.
///
/// The two cadences here are deliberate. Counters are cheap native calls and are read every
/// second; sensors go through WMI or NVML, cost orders of magnitude more, and do not move
/// meaningfully inside a second, so they are read every fifth tick.
/// </summary>
public sealed partial class LiveCountersViewModel : ObservableObject
{
    private const int HistoryLength = 60;

    /// <summary>Sensors are read every fifth tick; WMI and NVML are far heavier than a counter read.</summary>
    private const int SensorEveryNthTick = 5;

    private readonly ISystemInfoService _systemInfo;
    private readonly ISensorService _sensors;

    private int _tickCount;

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private long _ramUsedMb;

    [ObservableProperty]
    private long _ramTotalMb;

    [ObservableProperty]
    private double _ramUsagePercent;

    [ObservableProperty]
    private long _standbyMb;

    [ObservableProperty]
    private int _processCount;

    [ObservableProperty]
    private long _driveFreeMb;

    [ObservableProperty]
    private long _driveTotalMb;

    [ObservableProperty]
    private TimeSpan _uptime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCpuTemperature))]
    [NotifyPropertyChangedFor(nameof(HasAnySensor))]
    private int? _cpuTemperature;

    [ObservableProperty]
    private string _cpuTemperatureSource = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGpuTemperature))]
    [NotifyPropertyChangedFor(nameof(HasAnySensor))]
    private int? _gpuTemperature;

    [ObservableProperty]
    private string _gpuTemperatureSource = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGpuUsage))]
    private int? _gpuUsage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnySensor))]
    private bool _sensorsChecked;

    public LiveCountersViewModel(ISystemInfoService systemInfo, ISensorService sensors)
    {
        _systemInfo = systemInfo;
        _sensors = sensors;
    }

    public ObservableCollection<double> CpuHistory { get; } = [];

    public ObservableCollection<double> RamHistory { get; } = [];

    public bool HasCpuTemperature => CpuTemperature is not null;

    public bool HasGpuTemperature => GpuTemperature is not null;

    public bool HasGpuUsage => GpuUsage is not null;

    /// <summary>
    /// False once a sample has come back with nothing. The card then explains why rather than
    /// showing an empty box - or worse, a zero that reads like a real temperature.
    /// </summary>
    public bool HasAnySensor => !SensorsChecked || HasCpuTemperature || HasGpuTemperature;

    /// <summary>One second's worth of readings. Sensors only come along for every fifth one.</summary>
    public void Tick()
    {
        if (_tickCount++ % SensorEveryNthTick == 0)
        {
            _ = SampleSensorsAsync();
        }

        SystemSnapshot snapshot = _systemInfo.GetSnapshot();

        CpuUsage = Math.Round(snapshot.CpuUsagePercent, 1);
        RamUsedMb = snapshot.RamUsedMb;
        RamTotalMb = snapshot.RamTotalMb;
        RamUsagePercent = Math.Round(snapshot.RamUsagePercent, 1);
        StandbyMb = snapshot.StandbyMemoryMb;
        ProcessCount = snapshot.ProcessCount;
        DriveFreeMb = snapshot.SystemDriveFreeMb;
        DriveTotalMb = snapshot.SystemDriveTotalMb;
        Uptime = snapshot.Uptime;

        Append(CpuHistory, snapshot.CpuUsagePercent);
        Append(RamHistory, snapshot.RamUsagePercent);
    }

    public async Task SampleSensorsAsync()
    {
        try
        {
            SensorReadings readings = await _sensors.ReadAsync().ConfigureAwait(true);

            CpuTemperature = readings.Cpu?.Rounded;
            CpuTemperatureSource = readings.Cpu?.Source ?? string.Empty;
            GpuTemperature = readings.Gpu?.Rounded;
            GpuTemperatureSource = readings.Gpu?.Source ?? string.Empty;
            GpuUsage = readings.GpuUsagePercent;
            SensorsChecked = true;
        }
        catch (Exception)
        {
            // A sensor that will not answer is a missing tile, never a broken dashboard.
            SensorsChecked = true;
        }
    }

    private static void Append(ObservableCollection<double> series, double value)
    {
        series.Add(value);

        while (series.Count > HistoryLength)
        {
            series.RemoveAt(0);
        }
    }
}
