using CommunityToolkit.Mvvm.ComponentModel;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// What this machine is: processor, graphics, memory, board, drive, Windows build.
///
/// Read once, when the dashboard is first opened, and never again - none of it changes while the
/// application is running. That is the whole reason it is not mixed in with the counters next
/// door, which change every second.
/// </summary>
public sealed partial class HardwareCardViewModel : ObservableObject
{
    private readonly ISystemInfoService _systemInfo;

    [ObservableProperty]
    private string _cpuName = string.Empty;

    [ObservableProperty]
    private string _gpuName = string.Empty;

    [ObservableProperty]
    private string _gpuDetail = string.Empty;

    [ObservableProperty]
    private string _memorySummary = string.Empty;

    [ObservableProperty]
    private string _windowsSummary = string.Empty;

    [ObservableProperty]
    private string _motherboard = string.Empty;

    [ObservableProperty]
    private string _driveModel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuTopology))]
    private int _cpuCores;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuTopology))]
    private int _cpuThreads;

    public HardwareCardViewModel(ISystemInfoService systemInfo) => _systemInfo = systemInfo;

    public string CpuTopology => CpuCores > 0 ? $"{CpuCores}C / {CpuThreads}T" : string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        HardwareInfo hardware = await _systemInfo.GetHardwareInfoAsync(cancellationToken).ConfigureAwait(true);

        CpuName = hardware.CpuName;
        GpuName = hardware.GpuName;
        GpuDetail = hardware.GpuVramMb > 0
            ? $"{hardware.GpuVramMb / 1024.0:0.#} GB · {hardware.GpuDriverVersion}"
            : hardware.GpuDriverVersion;
        MemorySummary = hardware.RamSummary;
        WindowsSummary = hardware.Windows.ToString();
        Motherboard = hardware.MotherboardName;
        DriveModel = hardware.SystemDriveModel;
        CpuCores = hardware.CpuCores;
        CpuThreads = hardware.CpuThreads;
    }
}
