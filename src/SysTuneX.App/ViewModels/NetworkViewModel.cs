using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.ViewModels;

/// <summary>The network page: latency tweaks plus the DNS card.</summary>
public sealed partial class NetworkViewModel : TweakPageViewModel
{
    private readonly INetworkService _network;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAdapter))]
    private NetworkAdapterInfo? _selectedAdapter;

    [ObservableProperty]
    private DnsPresetViewModel? _selectedPreset;

    [ObservableProperty]
    private string _currentDns = string.Empty;

    [ObservableProperty]
    private bool _isDnsBusy;

    [ObservableProperty]
    private string _latency = string.Empty;

    [ObservableProperty]
    private bool _isMeasuring;

    public NetworkViewModel(
        ITweakEngine tweaks,
        IEnvironmentService environment,
        IUserInteraction interaction,
        ILocalizationService localization,
        CatalogText text,
        IAppSettingsService settings,
        INetworkService network)
        : base(tweaks, environment, interaction, localization, text, settings)
    {
        _network = network;

        foreach (DnsPreset preset in NetworkTweaks.DnsPresets)
        {
            Presets.Add(new DnsPresetViewModel(preset));
        }
    }

    protected override TweakCategory Category => TweakCategory.Network;

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];

    public ObservableCollection<DnsPresetViewModel> Presets { get; } = [];

    public bool HasAdapter => SelectedAdapter is not null;

    protected override async Task OnEnterAsync()
    {
        await base.OnEnterAsync().ConfigureAwait(true);
        LoadAdapters();
    }

    partial void OnSelectedAdapterChanged(NetworkAdapterInfo? value) => RefreshDns();

    partial void OnSelectedPresetChanged(DnsPresetViewModel? value)
    {
        // The radio buttons bind one-way to IsSelected, so the whole set is kept in sync here
        // rather than letting each button write back into the view model.
        foreach (DnsPresetViewModel preset in Presets)
        {
            preset.IsSelected = ReferenceEquals(preset, value);
        }
    }

    [RelayCommand]
    private void SelectPreset(DnsPresetViewModel? preset) => SelectedPreset = preset;

    [RelayCommand]
    private void ReloadAdapters() => LoadAdapters();

    [RelayCommand]
    private async Task ApplyDnsAsync()
    {
        if (SelectedAdapter is null || SelectedPreset is null || IsDnsBusy)
        {
            return;
        }

        IsDnsBusy = true;

        try
        {
            OperationResult result = await _network
                .SetDnsAsync(SelectedAdapter.Id, SelectedPreset.Primary, SelectedPreset.Secondary, PageToken)
                .ConfigureAwait(true);

            if (result.Success)
            {
                Interaction.ShowSuccess(Localization.Format("Msg_DnsApplied", SelectedPreset.Name));
            }
            else
            {
                Interaction.ShowError(result.Describe(Localization));
            }

            LoadAdapters();
        }
        finally
        {
            IsDnsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetDnsAsync()
    {
        if (SelectedAdapter is null || IsDnsBusy)
        {
            return;
        }

        IsDnsBusy = true;

        try
        {
            // Restore, not "set to DHCP": if the machine had static resolvers before SysTuneX
            // touched it, those are what should come back.
            OperationResult result = await _network
                .RestoreDnsAsync(SelectedAdapter.Id, PageToken)
                .ConfigureAwait(true);

            if (result.Success)
            {
                Interaction.ShowSuccess(Localization["Msg_DnsReset"]);
            }
            else
            {
                Interaction.ShowError(result.Describe(Localization));
            }

            LoadAdapters();
        }
        finally
        {
            IsDnsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FlushDnsAsync()
    {
        OperationResult result = await _network.FlushDnsCacheAsync(PageToken).ConfigureAwait(true);

        if (result.Success)
        {
            Interaction.ShowSuccess(Localization["Msg_DnsFlushed"]);
        }
        else
        {
            Interaction.ShowError(result.Describe(Localization));
        }
    }

    [RelayCommand]
    private async Task MeasureAsync()
    {
        if (IsMeasuring)
        {
            return;
        }

        IsMeasuring = true;
        Latency = Localization["Network_Measuring"];

        try
        {
            foreach (DnsPresetViewModel preset in Presets)
            {
                long? milliseconds = await _network.MeasureLatencyAsync(preset.Primary, PageToken).ConfigureAwait(true);
                preset.LatencyMs = milliseconds;
            }

            IReadOnlyList<string> current = SelectedAdapter is null ? [] : _network.GetDnsServers(SelectedAdapter.Id);
            long? currentLatency = current.Count > 0
                ? await _network.MeasureLatencyAsync(current[0], PageToken).ConfigureAwait(true)
                : null;

            Latency = currentLatency is null ? "—" : $"{currentLatency} ms";
        }
        catch (OperationCanceledException)
        {
            Latency = string.Empty;
        }
        finally
        {
            IsMeasuring = false;
        }
    }

    private void LoadAdapters()
    {
        string? previousId = SelectedAdapter?.Id;

        Adapters.Clear();

        foreach (NetworkAdapterInfo adapter in _network.GetActiveAdapters())
        {
            Adapters.Add(adapter);
        }

        SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == previousId) ?? Adapters.FirstOrDefault();
        RefreshDns();
    }

    private void RefreshDns()
    {
        if (SelectedAdapter is null)
        {
            CurrentDns = string.Empty;
            return;
        }

        IReadOnlyList<string> servers = _network.GetDnsServers(SelectedAdapter.Id);

        CurrentDns = servers.Count == 0
            ? Localization["Network_Dns_Automatic"]
            : SelectedAdapter.UsesDhcpForDns
                ? $"{string.Join(", ", servers)} ({Localization["Network_Dns_Automatic"]})"
                : string.Join(", ", servers);
    }
}

public sealed partial class DnsPresetViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LatencyText))]
    private long? _latencyMs;

    public DnsPresetViewModel(DnsPreset preset) => Preset = preset;

    public DnsPreset Preset { get; }

    public string Id => Preset.Id;

    public string Name => Preset.Name;

    public string Primary => Preset.Primary;

    public string Secondary => Preset.Secondary;

    public string Servers => $"{Preset.Primary} · {Preset.Secondary}";

    public string LatencyText => LatencyMs is null ? string.Empty : $"{LatencyMs} ms";
}
