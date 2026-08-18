using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tweaks;

/// <summary>
/// The single list of every tweak SysTuneX knows about.
///
/// The previous build kept parallel arrays per page and shipped two different tweaks with the
/// id <c>service_kill_timeout</c> and two with <c>sfio_priority</c>, so a profile referring to
/// one of them silently applied whichever happened to be found first. Ids are now verified to
/// be unique the first time the catalog is touched.
/// </summary>
public static class TweakCatalog
{
    private static readonly Lazy<IReadOnlyList<TweakDefinition>> LazyAll = new(Build);
    private static readonly Lazy<IReadOnlyDictionary<string, TweakDefinition>> LazyById =
        new(() => LazyAll.Value.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<TweakDefinition> All => LazyAll.Value;

    public static TweakDefinition? Find(string id) =>
        LazyById.Value.TryGetValue(id, out TweakDefinition? tweak) ? tweak : null;

    public static IEnumerable<TweakDefinition> InCategory(TweakCategory category) =>
        All.Where(t => t.Category == category);

    private static IReadOnlyList<TweakDefinition> Build()
    {
        List<TweakDefinition> all =
        [
            .. GamingTweaks.All,
            .. Windows11Tweaks.All,
            .. PrivacyTweaks.All,
            .. NetworkTweaks.All,
        ];

        string[] duplicates = all
            .GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Tweak ids must be unique across the catalog. Duplicated: {string.Join(", ", duplicates)}.");
        }

        // A registry tweak with nothing to write and no handler would silently report success.
        string[] empty = all
            .Where(t => t.Changes.Count == 0 && t.HandlerKey is null)
            .Select(t => t.Id)
            .ToArray();

        if (empty.Length > 0)
        {
            throw new InvalidOperationException(
                $"These tweaks define no changes and no handler: {string.Join(", ", empty)}.");
        }

        return all;
    }
}
