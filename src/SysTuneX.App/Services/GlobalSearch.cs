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
    private readonly IReadOnlyList<ISearchSource> _sources;

    public GlobalSearch(IEnumerable<ISearchSource> sources) => _sources = [.. sources];

    public IReadOnlyList<SearchHit> Search(string query, int limit = 12)
    {
        string needle = (query ?? string.Empty).Trim();
        if (needle.Length < 2)
        {
            // One letter matches most of the catalog, which is not a search result, it is noise.
            return [];
        }

        return [.. _sources
            .SelectMany(source => source.Candidates())
            .Select(candidate => (Candidate: candidate, Score: Score(candidate, needle)))
            .Where(scored => scored.Score > 0)
            .OrderByDescending(scored => scored.Score)
            .ThenBy(scored => scored.Candidate.Hit.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .Select(scored => scored.Candidate.Hit)];
    }

    /// <summary>
    /// Higher is better. A name match beats a description match, and a match on the identifier
    /// is worth as much as the name: someone who knows the value is called HwSchMode should not
    /// have to guess what the tweak was named in their language.
    /// </summary>
    private static int Score(SearchCandidate candidate, string needle)
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
}
