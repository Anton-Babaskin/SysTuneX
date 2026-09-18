using System.Xml.Linq;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The catalogues are the one part of the interface whose English is not in a resource file.
///
/// Every tweak, service, profile and cleanup target carries neutral English in C# - so Core stays
/// useful and testable on its own - and <c>CatalogText</c> looks for a translated resource first,
/// falling back to that text. It is a good arrangement and it has one blind spot: a missing
/// translation degrades to English silently, which is the correct behaviour on a user's machine
/// and the wrong behaviour on a developer's, because nothing ever says a translation is missing.
///
/// So the English half is checked here - it is the fallback everything else rests on - and the
/// translated halves are checked against the catalogue rather than only against each other. The
/// string coverage tests next door cannot do this: they compare resource files, and the English
/// catalogue is not in one.
/// </summary>
public sealed class CatalogTranslationTests
{
    private const string ResourceRoot = "../../../../../src/SysTuneX.App/Resources";

    public static TheoryData<string> Translations =>
        [.. Directory.EnumerateFiles(ResourceRoot, "Strings.*.resx")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];

    /// <summary>
    /// Every catalogue entry, with the resource prefix its translations use and the English text
    /// that shows when there is no translation.
    /// </summary>
    private static IEnumerable<(string Key, string English)> Entries()
    {
        foreach (TweakDefinition tweak in TweakCatalog.All)
        {
            yield return ($"Tweak_{tweak.Id}_Name", tweak.Name);
            yield return ($"Tweak_{tweak.Id}_Desc", tweak.Description);
        }

        foreach (ServiceDefinition service in ServiceCatalog.All)
        {
            yield return ($"Service_{service.ServiceName}_Name", service.DisplayName);
            yield return ($"Service_{service.ServiceName}_Desc", service.Description);
        }

        foreach (GameProfile profile in GameProfiles.BuiltIn)
        {
            yield return ($"Profile_{profile.Id}_Name", profile.Name);
            yield return ($"Profile_{profile.Id}_Desc", profile.Description);
        }

        foreach (CleanupTarget target in CleanupCatalog.All)
        {
            yield return ($"Cleanup_{target.Id}_Name", target.Name);
            yield return ($"Cleanup_{target.Id}_Desc", target.Description);
        }
    }

    /// <summary>
    /// The English interface. It is not in a resource file, so nothing else checks it - and a
    /// catalogue entry with no description is an empty paragraph on the page for every English
    /// user, while every translated user reads a full one.
    /// </summary>
    [Fact]
    public void Every_catalogue_entry_carries_its_English_text()
    {
        List<string> blank =
        [
            .. Entries()
                .Where(entry => string.IsNullOrWhiteSpace(entry.English))
                .Select(entry => entry.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(blank);
    }

    /// <summary>
    /// English is what a missing translation falls back to, so it has to read as finished text
    /// rather than as a placeholder somebody meant to come back to.
    /// </summary>
    [Fact]
    public void No_English_catalogue_text_is_a_placeholder()
    {
        string[] placeholders = ["TODO", "TBD", "FIXME", "XXX", "Lorem", "..."];

        List<string> suspect =
        [
            .. Entries()
                .Where(entry => placeholders.Any(p => entry.English.Contains(p, StringComparison.OrdinalIgnoreCase)))
                .Select(entry => $"{entry.Key}: {entry.English}")
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(suspect);
    }

    /// <summary>
    /// A translation that is missing an entry shows English on that one line, in the middle of a
    /// page that is otherwise translated - which reads as a bug rather than as a gap.
    /// </summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_catalogue_entry_is_translated(string resx)
    {
        HashSet<string> translated = Keys(resx);

        List<string> missing =
        [
            .. Entries()
                .Select(entry => entry.Key)
                .Where(key => !translated.Contains(key))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(missing);
    }

    /// <summary>
    /// The other direction: a translation for an id the catalogue no longer has. It costs nothing
    /// at run time, which is exactly why it accumulates - a renamed tweak leaves its old name and
    /// description behind in every language, to be carried and re-reviewed forever.
    /// </summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void No_translation_names_a_catalogue_entry_that_is_gone(string resx)
    {
        string[] prefixes = ["Tweak_", "Service_", "Profile_", "Cleanup_"];
        HashSet<string> real = [.. Entries().Select(entry => entry.Key)];

        List<string> orphans =
        [
            .. Keys(resx)
                .Where(key => prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)))
                .Where(key => key.EndsWith("_Name", StringComparison.Ordinal) ||
                              key.EndsWith("_Desc", StringComparison.Ordinal))
                .Where(key => !real.Contains(key))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(orphans);
    }

    /// <summary>
    /// Guards the scans above: an empty catalogue or an empty harvest would pass all four.
    /// </summary>
    [Fact]
    public void The_catalogue_and_the_language_files_are_found()
    {
        List<(string Key, string English)> entries = [.. Entries()];

        Assert.True(entries.Count >= 150, $"Only {entries.Count} catalogue entries were found.");
        Assert.Contains(entries, e => e.Key == "Tweak_game_bar_disable_Name");
        Assert.Contains(entries, e => e.Key == "Service_DiagTrack_Desc");

        List<string> languages =
        [
            .. Directory.EnumerateFiles(ResourceRoot, "Strings.*.resx").Select(Path.GetFileName).OfType<string>(),
        ];

        Assert.Contains("Strings.ru.resx", languages);
        Assert.True(languages.Count >= 2, $"Only {languages.Count} translation(s) were found.");
        Assert.True(Keys("Strings.ru.resx").Count >= 200, "The Russian key harvest came back nearly empty.");
    }

    private static HashSet<string> Keys(string resx) =>
    [
        .. XDocument.Load(Path.Combine(ResourceRoot, resx))
            .Root!
            .Elements("data")
            .Select(data => data.Attribute("name")?.Value)
            .OfType<string>(),
    ];
}
