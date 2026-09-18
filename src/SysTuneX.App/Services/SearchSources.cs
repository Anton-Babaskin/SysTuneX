using SysTuneX.App.Localization;
using SysTuneX.App.Views.Pages;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.Services;

/// <param name="Hit">What the user sees and where it takes them.</param>
/// <param name="Identifier">
/// The internal name - a tweak id, a service name. Matched as well as the title, so someone who
/// knows the value is called HwSchMode does not have to guess what it was called in their language.
/// </param>
public sealed record SearchCandidate(SearchHit Hit, string Identifier);

/// <summary>
/// One thing global search can find.
///
/// Three sources were written into the search itself, which meant adding a fourth - startup items,
/// say - meant editing the search rather than adding a class. The scoring is the search's job; what
/// there is to score is not.
/// </summary>
public interface ISearchSource
{
    IEnumerable<SearchCandidate> Candidates();
}

/// <summary>
/// Which page owns a tweak category, declared once.
///
/// This used to be a switch inside global search whose default arm was Gaming, so a category
/// added to the enum and not to the switch opened the wrong page - silently, with the user left
/// looking at a list that does not contain what they searched for. A category with no registration
/// is now simply not findable, which is visibly wrong rather than quietly wrong, and a test fails
/// the moment one is added without a page.
/// </summary>
/// <param name="NavigationKey">Resource key for the page's name in the navigation pane.</param>
public sealed record TweakCategoryPage(TweakCategory Category, Type Page, string NavigationKey);

/// <summary>Every tweak the running build supports.</summary>
public sealed class TweakSearchSource : ISearchSource
{
    private readonly ITweakEngine _tweaks;
    private readonly ILocalizationService _localization;
    private readonly CatalogText _text;
    private readonly IReadOnlyDictionary<TweakCategory, TweakCategoryPage> _pages;

    public TweakSearchSource(
        ITweakEngine tweaks,
        ILocalizationService localization,
        CatalogText text,
        IEnumerable<TweakCategoryPage> pages)
    {
        _tweaks = tweaks;
        _localization = localization;
        _text = text;
        _pages = pages.ToDictionary(p => p.Category);
    }

    public IEnumerable<SearchCandidate> Candidates()
    {
        foreach (TweakDefinition tweak in _tweaks.GetSupportedTweaks())
        {
            if (!_pages.TryGetValue(tweak.Category, out TweakCategoryPage? page))
            {
                // No page owns this category. Sending the user to a page that cannot contain the
                // result is worse than not finding it.
                continue;
            }

            string name = _text.Name(tweak);

            yield return new SearchCandidate(
                new SearchHit(
                    name,
                    _text.Description(tweak),
                    _localization[page.NavigationKey],
                    page.Page,
                    name,
                    tweak.Risk),
                tweak.Id);
        }
    }
}

/// <summary>Services this build of Windows actually has.</summary>
public sealed class ServiceSearchSource : ISearchSource
{
    private readonly IEnvironmentService _environment;
    private readonly ILocalizationService _localization;
    private readonly CatalogText _text;

    public ServiceSearchSource(IEnvironmentService environment, ILocalizationService localization, CatalogText text)
    {
        _environment = environment;
        _localization = localization;
        _text = text;
    }

    public IEnumerable<SearchCandidate> Candidates()
    {
        foreach (ServiceDefinition service in ServiceCatalog.All)
        {
            // Services the running build does not have are not findable, because they are not on
            // the services page either.
            if (_environment.Windows.Build < service.MinBuild)
            {
                continue;
            }

            string name = _text.Name(service);

            yield return new SearchCandidate(
                new SearchHit(
                    name,
                    _text.Description(service),
                    _localization["Nav_Services"],
                    typeof(ServicesPage),
                    name,
                    service.Risk),
                service.ServiceName);
        }
    }
}

/// <summary>Cleanup targets.</summary>
public sealed class CleanupSearchSource : ISearchSource
{
    private readonly ILocalizationService _localization;
    private readonly CatalogText _text;

    public CleanupSearchSource(ILocalizationService localization, CatalogText text)
    {
        _localization = localization;
        _text = text;
    }

    public IEnumerable<SearchCandidate> Candidates()
    {
        foreach (CleanupTarget target in CleanupCatalog.All)
        {
            yield return new SearchCandidate(
                new SearchHit(
                    _text.Name(target),
                    _text.Description(target),
                    _localization["Nav_Cleanup"],
                    typeof(CleanupPage),
                    string.Empty,
                    null),
                target.Id);
        }
    }
}
