using CommunityToolkit.Mvvm.ComponentModel;

namespace SysTuneX.Core.Models;

public partial class ServiceItem : ObservableObject
{
    public string ServiceName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public RiskLevel Risk { get; init; }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isBusy;
}
