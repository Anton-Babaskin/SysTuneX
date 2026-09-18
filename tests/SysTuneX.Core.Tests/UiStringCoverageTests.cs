using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The interface asks for its strings by name at run time, so a typo or a key added in one
/// language only compiles perfectly and shows a raw resource key on screen. These checks used to
/// live in a throwaway script run by hand; here they run on every build, which is the difference
/// between a guard and a good intention.
/// </summary>
public sealed partial class UiStringCoverageTests
{
    private const string AppRoot = "../../../../../src/SysTuneX.App";
    private const string CoreRoot = "../../../../../src/SysTuneX.Core";

    /// <summary><c>{loc:Loc Some_Key}</c> in XAML.</summary>
    [GeneratedRegex(@"\{loc:Loc\s+([A-Za-z0-9_]+)")]
    private static partial Regex MarkupKey { get; }

    /// <summary><c>_localization["Some_Key"]</c> and the Get/Format overloads in C#.</summary>
    [GeneratedRegex(@"[Ll]ocalization(?:\[\s*|\.(?:Get|Format)\(\s*)""([A-Za-z0-9_]+)""")]
    private static partial Regex CodeKey { get; }

    /// <summary>
    /// Every language file there is, found rather than listed.
    ///
    /// Listing them meant adding a language was two edits in two projects, and forgetting the
    /// second one left the new language with none of these checks - which is exactly when they
    /// matter most.
    /// </summary>
    public static TheoryData<string> Languages => [.. LanguageFiles()];

    /// <summary>The translations, without the English original they are translations of.</summary>
    public static TheoryData<string> Translations =>
        [.. LanguageFiles().Where(name => !string.Equals(name, English, StringComparison.Ordinal))];

    private const string English = "Strings.resx";

    private static IEnumerable<string> LanguageFiles() =>
        Directory.EnumerateFiles(Path.Combine(AppRoot, "Resources"), "Strings*.resx")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// Prefixes whose keys are built at run time from an enum member or a catalogue id, so no
    /// literal ever appears in the source: <c>$"MetricGroup_{definition.Group}"</c> and friends.
    /// Every entry here is a real call site; adding one to silence a failure rather than to
    /// describe such a call site would defeat the check below.
    /// </summary>
    private static readonly string[] BuiltAtRunTime =
    [
        "MetricGroup_",   // MonitorViewModel, from MonitorGroup
        "Core_",          // OperationResultText, from a CoreMessages code
        "History_Kind_",  // CatalogText, from BackupKind
        "Risk_",          // UserInteraction, from RiskLevel
        "Tweak_",         // CatalogText, from a tweak id
        "Profile_",       // CatalogText, from a profile id
        "Service_",       // CatalogText, from a service name
        "Cleanup_",       // CatalogText, from a cleanup target id
    ];

    /// <summary>
    /// The other half of <see cref="Every_key_the_interface_asks_for_exists"/>.
    ///
    /// A string nothing asks for still has to be translated, reviewed and carried in both files
    /// forever. Twenty of them had accumulated before this test existed - left behind by pages
    /// that were rewritten and features that were renamed, and invisible because the build is
    /// perfectly happy to carry them.
    /// </summary>
    [Fact]
    public void Every_string_that_is_defined_is_used()
    {
        // Both projects, because a key can be asked for by the interface or named by a catalogue
        // entry in the core - MonitorMetrics and the tweak definitions both carry resource keys.
        string source = string.Concat(Sources().Concat(CoreSources()).Select(pair => pair.Text));

        List<string> unused = Keys("Strings.resx")
            .Where(key => !BuiltAtRunTime.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .Where(key => !source.Contains(key, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unused);
    }

    [Fact]
    public void Every_key_the_interface_asks_for_exists()
    {
        HashSet<string> defined = Keys("Strings.resx");
        var missing = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach ((string file, string text) in Sources())
        {
            Regex pattern = file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ? MarkupKey : CodeKey;

            foreach (Match match in pattern.Matches(text))
            {
                string key = match.Groups[1].Value;
                if (!defined.Contains(key))
                {
                    missing[key] = Path.GetFileName(file);
                }
            }
        }

        Assert.Empty(missing.Select(pair => $"{pair.Key} (used in {pair.Value})"));
    }

    /// <summary>
    /// A key present in English but missing from a translation falls back to English, which reads
    /// as a bug rather than as a translation gap - and nobody notices until a user screenshots it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_language_carries_the_same_keys(string resx)
    {
        HashSet<string> english = Keys(English);
        HashSet<string> translated = Keys(resx);

        Assert.Empty(english.Except(translated, StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The catalogue's own names and descriptions are English in C# and translated here, so a
    /// translation carries keys the English file does not. Which is fine - but it also means a
    /// second translation can silently miss a whole catalogue, so the translations are held to
    /// each other as well as to the original.
    /// </summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_language_carries_the_same_catalogue_entries(string resx)
    {
        var everyCatalogueKey = new HashSet<string>(StringComparer.Ordinal);

        foreach (string language in LanguageFiles().Where(f => !string.Equals(f, English, StringComparison.Ordinal)))
        {
            everyCatalogueKey.UnionWith(
                Keys(language).Where(key => BuiltAtRunTime.Any(p => key.StartsWith(p, StringComparison.Ordinal))));
        }

        Assert.Empty(everyCatalogueKey.Except(Keys(resx), StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    /// <summary>Guards the discovery above: finding no languages would pass everything trivially.</summary>
    [Fact]
    public void The_languages_are_found()
    {
        List<string> found = [.. LanguageFiles()];

        Assert.Contains(English, found);
        Assert.Contains("Strings.ru.resx", found);
        Assert.True(found.Count >= 2, $"Only {found.Count} language file(s) were found.");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_string_is_left_blank(string resx)
    {
        List<string> blank = Entries(resx)
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(blank);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void No_key_is_defined_twice(string resx)
    {
        List<string> duplicates = RawKeys(resx)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(duplicates);
    }

    /// <summary>
    /// Placeholders have to agree across languages: a translation with one fewer <c>{0}</c>
    /// silently drops a value, and one with an extra throws in front of the user.
    /// </summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void A_translation_takes_the_same_arguments_as_the_original(string resx)
    {
        Dictionary<string, string> english = Entries(English);
        Dictionary<string, string> translated = Entries(resx);

        var wrong = new List<string>();

        foreach ((string key, string text) in english)
        {
            if (!translated.TryGetValue(key, out string? other))
            {
                continue;
            }

            int expected = PlaceholderCount(text);
            int actual = PlaceholderCount(other);

            if (expected != actual)
            {
                wrong.Add($"{key}: English uses {expected} placeholder(s), {resx} uses {actual}");
            }
        }

        Assert.Empty(wrong);
    }

    private static int PlaceholderCount(string text)
    {
        int highest = -1;
        for (int index = 0; index < 10; index++)
        {
            if (text.Contains($"{{{index}}}", StringComparison.Ordinal))
            {
                highest = index;
            }
        }

        return highest + 1;
    }

    private static IEnumerable<(string File, string Text)> Sources() => Sources(AppRoot);

    /// <summary>The core names resource keys too - the metric catalogue and the tweak warnings.</summary>
    private static IEnumerable<(string File, string Text)> CoreSources() => Sources(CoreRoot);

    private static IEnumerable<(string File, string Text)> Sources(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            // obj and bin hold generated copies of the same markup; scanning them would double
            // every finding and report file names nobody can open.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            if (file.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                yield return (file, File.ReadAllText(file));
            }
        }
    }

    private static HashSet<string> Keys(string resx) => [.. RawKeys(resx)];

    private static IEnumerable<string> RawKeys(string resx) =>
        XDocument.Load(Path.Combine(AppRoot, "Resources", resx))
            .Root!
            .Elements("data")
            .Select(data => data.Attribute("name")?.Value)
            .Where(name => name is not null)!;

    private static Dictionary<string, string> Entries(string resx) =>
        XDocument.Load(Path.Combine(AppRoot, "Resources", resx))
            .Root!
            .Elements("data")
            .Where(data => data.Attribute("name") is not null)
            .GroupBy(data => data.Attribute("name")!.Value, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
}
