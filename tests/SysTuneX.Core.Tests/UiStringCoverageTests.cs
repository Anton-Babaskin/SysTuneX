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

    /// <summary><c>{loc:Loc Some_Key}</c> in XAML.</summary>
    [GeneratedRegex(@"\{loc:Loc\s+([A-Za-z0-9_]+)")]
    private static partial Regex MarkupKey { get; }

    /// <summary><c>_localization["Some_Key"]</c> and the Get/Format overloads in C#.</summary>
    [GeneratedRegex(@"[Ll]ocalization(?:\[\s*|\.(?:Get|Format)\(\s*)""([A-Za-z0-9_]+)""")]
    private static partial Regex CodeKey { get; }

    public static TheoryData<string> Languages => ["Strings.resx", "Strings.ru.resx"];

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
    /// A key present in English but not Russian falls back to English, which reads as a bug
    /// rather than as a translation gap - and nobody notices until a user screenshots it.
    /// </summary>
    [Fact]
    public void Both_languages_carry_the_same_keys()
    {
        HashSet<string> english = Keys("Strings.resx");
        HashSet<string> russian = Keys("Strings.ru.resx");

        Assert.Empty(english.Except(russian, StringComparer.Ordinal).Order(StringComparer.Ordinal));
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
    [Fact]
    public void Translations_take_the_same_arguments_as_the_original()
    {
        Dictionary<string, string> english = Entries("Strings.resx");
        Dictionary<string, string> russian = Entries("Strings.ru.resx");

        var wrong = new List<string>();

        foreach ((string key, string text) in english)
        {
            if (!russian.TryGetValue(key, out string? translated))
            {
                continue;
            }

            int expected = PlaceholderCount(text);
            int actual = PlaceholderCount(translated);

            if (expected != actual)
            {
                wrong.Add($"{key}: English uses {expected} placeholder(s), Russian uses {actual}");
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

    private static IEnumerable<(string File, string Text)> Sources()
    {
        foreach (string file in Directory.EnumerateFiles(AppRoot, "*.*", SearchOption.AllDirectories))
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
