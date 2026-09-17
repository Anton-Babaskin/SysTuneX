using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// A <c>{StaticResource}</c> naming a key that does not exist is not a build error. It throws when
/// the page is parsed - which is when the user opens it - and takes the whole page with it.
///
/// This project has already paid for exactly that: a pack URI to an image that was never compiled
/// in shipped green through build and publish and killed the app on a user's desktop with
/// "Provide value on 'TypeConverterMarkupExtension' threw an exception". The layout smoke tests
/// catch it now, but only on Windows, so a resource renamed on any other machine gets found by the
/// person who opens the page rather than by the person who renamed it.
///
/// This check needs no Windows and no WPF: it is text in, text out.
/// </summary>
public sealed partial class ResourceCoverageTests
{
    private const string AppRoot = "../../../../../src/SysTuneX.App";

    /// <summary><c>{StaticResource Foo}</c> and <c>{DynamicResource Foo}</c>.</summary>
    [GeneratedRegex(@"\{(?:Static|Dynamic)Resource\s+(?:ResourceKey=)?([A-Za-z_][\w.]*)\s*\}")]
    private static partial Regex ResourceUse { get; }

    /// <summary><c>x:Key="Foo"</c> in any of the app's dictionaries.</summary>
    [GeneratedRegex(@"x:Key=""([A-Za-z_][\w.]*)""")]
    private static partial Regex ResourceKey { get; }

    /// <summary>
    /// Keys that come from somewhere this scan cannot see: the WPF UI theme dictionaries and WPF's
    /// own system resources. Everything the app defines itself has to be found in the app.
    ///
    /// Matched as prefixes because these families are large and grow with the library. A key that
    /// does not start with one of these is ours, and ours is what this test is about.
    /// </summary>
    private static readonly string[] SuppliedByTheme =
    [
        "TextFillColor", "AccentTextFillColor", "AccentFillColor", "ControlFillColor",
        "ControlStrokeColor", "ControlElevationBorder", "CardBackgroundFillColor", "CardStrokeColor",
        "DividerStrokeColor", "SolidBackgroundFillColor", "SubtleFillColor", "SystemFillColor",
        "ApplicationBackgroundBrush", "LayerFillColor", "SmokeFillColor", "KeyboardFocusBorder",
        "FocusStrokeColor", "TextOnAccentFillColor", "SystemAccentColor",
    ];

    [Fact]
    public void Every_resource_a_page_asks_for_is_defined()
    {
        HashSet<string> defined = DefinedKeys();

        var missing = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach ((string file, string text) in Files())
        {
            foreach (Match match in ResourceUse.Matches(text))
            {
                string key = match.Groups[1].Value;

                if (defined.Contains(key) ||
                    SuppliedByTheme.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    continue;
                }

                missing[key] = Path.GetFileName(file);
            }
        }

        Assert.Empty(missing.Select(pair => $"{pair.Key} (used in {pair.Value})"));
    }

    /// <summary>
    /// Guards the scan above. A harvest that silently collected nothing would make every resource
    /// look missing, or - if the *use* regex broke instead - make every page look clean.
    /// </summary>
    [Fact]
    public void The_scan_finds_what_it_should()
    {
        HashSet<string> defined = DefinedKeys();

        Assert.Contains("Card", defined);
        Assert.Contains("PageTitle", defined);
        Assert.Contains("TitleAccent", defined);
        Assert.True(defined.Count >= 25, $"Only {defined.Count} resource keys were found.");

        string dashboard = File.ReadAllText(Path.Combine(AppRoot, "Views/Pages/DashboardPage.xaml"));
        Assert.Contains("Card", ResourceUse.Matches(dashboard).Select(m => m.Groups[1].Value));
    }

    /// <summary>
    /// Every page opens with the same three things: a title, a subtitle and the accent rule under
    /// them. A page that skips the rule is not broken, it just quietly looks like a different
    /// application - which is the kind of drift nobody notices until the screenshots disagree.
    /// </summary>
    [Fact]
    public void Every_page_carries_the_title_accent()
    {
        List<string> without =
        [
            .. Files()
                .Where(pair => pair.File.Contains("Views", StringComparison.Ordinal) &&
                               pair.File.EndsWith("Page.xaml", StringComparison.Ordinal))
                .Where(pair => pair.Text.Contains("StaticResource PageTitle", StringComparison.Ordinal))
                .Where(pair => !pair.Text.Contains("StaticResource TitleAccent", StringComparison.Ordinal))
                .Select(pair => Path.GetFileName(pair.File)),
        ];

        Assert.Empty(without);
    }

    private static HashSet<string> DefinedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach ((_, string text) in Files())
        {
            foreach (Match match in ResourceKey.Matches(text))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
    }

    private static IEnumerable<(string File, string Text)> Files()
    {
        if (!Directory.Exists(AppRoot))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(AppRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (file, File.ReadAllText(file));
        }
    }

    /// <summary>
    /// XAML that is not well-formed XML cannot be parsed by anything, and the compiler's message
    /// for it points at a generated file rather than at the markup somebody just edited.
    /// </summary>
    [Fact]
    public void Every_xaml_file_is_well_formed()
    {
        var broken = new List<string>();

        foreach ((string file, string text) in Files())
        {
            try
            {
                XDocument.Parse(text);
            }
            catch (System.Xml.XmlException exception)
            {
                broken.Add($"{Path.GetFileName(file)}: {exception.Message}");
            }
        }

        Assert.Empty(broken);
    }
}
