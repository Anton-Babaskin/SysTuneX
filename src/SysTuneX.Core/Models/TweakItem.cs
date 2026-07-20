using CommunityToolkit.Mvvm.ComponentModel;

namespace SysTuneX.Core.Models;

public partial class TweakItem : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public TweakCategory Category { get; init; }
    public RiskLevel Risk { get; init; }

    [ObservableProperty]
    private TweakStatus _status = TweakStatus.Unknown;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isBusy;
}
