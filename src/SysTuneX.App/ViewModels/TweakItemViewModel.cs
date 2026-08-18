using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SysTuneX.App.Localization;
using SysTuneX.Core.Models;

namespace SysTuneX.App.ViewModels;

/// <summary>One row in a tweak list.</summary>
public sealed partial class TweakItemViewModel : ObservableObject
{
    private readonly CatalogText _text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApplied))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsToggleEnabled))]
    private TweakStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToggleEnabled))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _lastError;

    public TweakItemViewModel(TweakDefinition definition, TweakStatus status, CatalogText text)
    {
        Definition = definition;
        _text = text;
        _status = status;
    }

    public TweakDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Name => _text.Name(Definition);

    public string Description => _text.Description(Definition);

    public string RiskText => _text.Risk(Definition.Risk);

    public RiskLevel Risk => Definition.Risk;

    public string StatusText => _text.Status(Status);

    public bool RequiresRestart => Definition.RequiresRestart;

    public bool RequiresExplorerRestart => Definition.RequiresExplorerRestart;

    /// <summary>Bound to the toggle. Partial counts as off so the toggle re-applies the missing values.</summary>
    public bool IsApplied => Status == TweakStatus.Applied;

    public bool IsToggleEnabled => !IsBusy && Status != TweakStatus.Unsupported;

    /// <summary>Raises the property changes the search filter and the toggle depend on after a language switch.</summary>
    public void RefreshText()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(RiskText));
        OnPropertyChanged(nameof(StatusText));
    }

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query) ||
        Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        Description.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        Id.Contains(query, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A titled section of tweaks, matching <see cref="TweakDefinition.GroupKey"/>.</summary>
public sealed partial class TweakGroupViewModel : ObservableObject
{
    private readonly CatalogText _text;
    private readonly string _groupKey;

    [ObservableProperty]
    private bool _isVisible = true;

    public TweakGroupViewModel(string groupKey, CatalogText text)
    {
        _groupKey = groupKey;
        _text = text;
    }

    public string Title => _text.Group(_groupKey);

    public ObservableCollection<TweakItemViewModel> Items { get; } = [];

    /// <summary>Items left after the current search and filters, which is what the list binds to.</summary>
    public ObservableCollection<TweakItemViewModel> VisibleItems { get; } = [];

    public void RefreshText()
    {
        OnPropertyChanged(nameof(Title));

        foreach (TweakItemViewModel item in Items)
        {
            item.RefreshText();
        }
    }
}
