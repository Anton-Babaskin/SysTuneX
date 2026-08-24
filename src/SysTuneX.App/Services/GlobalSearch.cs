using SysTuneX.App.Localization;
using SysTuneX.App.Views.Pages;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.App.Services;

/// <summary>
/// One search across everything the app can change.
///
/// There are roughly a hundred tweaks spread over four pages, plus services and cleanup targets.
/// Knowing which page a setting lives on is the app's problem, not the user's.
/// </summary>
public interface IGlobalSearch
{
    IReadOnlyList<SearchHit> Search(string query, int limit = 12);
}

/// <param name="PageType">Page that owns the item, to navigate to.</param>
/// <param name="Filter">
/// Text to put in that page's own search box, so the item is the one thing on screen when the
/// page opens. Landing on a page of thirty rows with no idea which one matched is barely better
/// than not searching at all.
/// </param>
public sealed record SearchHit(
    string Title,
    string Subtitle,
    string Category,
    Type PageType,
    string Filter,
    RiskLevel? Risk);

/// <summary>A page whose own search box can be driven from outside.</summary>
public interface IFilterablePage
{
    string SearchText { get; set; }
}

/// <inheritdoc cref="IGlobalSearch"/>
public sealed class GlobalSearch : IGlobalSearch
{
    private readonly ITweakEngine _tweaks;
    private readonly IEnvironmentService _environment;
    private readonly ILocalizationService _localization;
    private readonly CatalogText _text;

    public GlobalSearch(
        ITweakEngine tweaks,
        IEnvironmentService environment,
        ILocalizationService localization,
        CatalogText text)
    {
        _tweaks = tweaks;
        _environment = environment;
        _localization = localization;
        _text = text;
    }

    public IReadOnlyList<SearchHit> Search(string query, int limit = 12)
    {
        string needle = (query ?? string.Empty).Trim();
        if (needle.Length < 2)
        {
            // One letter matches most of the catalog, which is not a search result, it is noise.
            return [];
        }

        return [.. Candidates()
            .Select(candidate => (Candidate: candidate, Score: Score(candidate, needle)))
            .Where(scored => scored.Score > 0)
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Candidate.Hit.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .Select(scored => scored.Candidate.Hit)];
    }

    private IEnumerable<(SearchHit Hit, string Identifier)> Candidates()
    {
        foreach (TweakDefinition tweak in _tweaks.GetSupportedTweaks())
        {
            string name = _text.Name(tweak);

            yield return (
                new SearchHit(
                    name,
                    _text.Description(tweak),
                    _localization[CategoryKey(tweak.Category)],
                    PageFor(tweak.Category),
                    name,
                    tweak.Risk),
                tweak.Id);
        }

        foreach (ServiceDefinition service in ServiceCatalog.All)
        {
            // Services the running build does not have are not findable, because they are not
            // on the services page either.
            if (_environment.Windows.Build < service.MinBuild)
            {
                continue;
            }

            string name = _text.Name(service);

            yield return (
                new SearchHit(
                    name,
                    _text.Description(service),
                    _localization["Nav_Services"],
                    typeof(ServicesPage),
                    name,
                    service.Risk),
                service.ServiceName);
        }

        foreach (CleanupTarget target in CleanupCatalog.All)
        {
            yield return (
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

    /// <summary>
    /// Higher is better. A name match beats a description match, and a match on the identifier
    /// is worth as much as the name: someone who knows the value is called HwSchMode should not
    /// have to guess what the tweak was named in their language.
    /// </summary>
    private static int Score((SearchHit Hit, string Identifier) candidate, string needle)
    {
        (SearchHit hit, string identifier) = candidate;

        if (hit.Title.StartsWith(needle, StringComparison.CurrentCultureIgnoreCase))
        {
            return 100;
        }

        if (hit.Title.Contains(needle, StringComparison.CurrentCultureIgnoreCase))
        {
            return 80;
        }

        if (identifier.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        return hit.Subtitle.Contains(needle, StringComparison.CurrentCultureIgnoreCase) ? 30 : 0;
    }

    private static string CategoryKey(TweakCategory category) => category switch
    {
        TweakCategory.Gaming => "Nav_Gaming",
        TweakCategory.Windows11 => "Nav_Windows11",
        TweakCategory.Privacy => "Nav_Privacy",
        TweakCategory.Network => "Nav_Network",
        _ => "Nav_Gaming",
    };

    private static Type PageFor(TweakCategory category) => category switch
    {
        TweakCategory.Gaming => typeof(GamingPage),
        TweakCategory.Windows11 => typeof(Windows11Page),
        TweakCategory.Privacy => typeof(PrivacyPage),
        TweakCategory.Network => typeof(NetworkPage),
        _ => typeof(GamingPage),
    };
}
