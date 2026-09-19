using System.Text.RegularExpressions;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Two rules the settings page cannot enforce for itself any more.
///
/// It used to be one class with one loading flag. Splitting it into a page and five sections spread
/// that flag across six files, and both halves of it now depend on every author remembering
/// something - which is the kind of thing that holds until the sixth section is added.
///
/// Both failures are silent, and both look like the application misbehaving on its own:
///
/// * A change handler that does not consult the loading flag fires while the page is filling its
///   controls in, because assigning a property raises the same notification a click does. Opening
///   the page then writes the settings file several times over and, worse, *applies* each value as
///   though it had just been chosen - re-registering the hotkey, rewriting the schedule, toggling
///   the tray icon.
/// * A section the page never loads shows its defaults instead of what is stored, and then writes
///   those defaults over the real settings the moment the user touches anything on it.
///
/// Text in, text out, like the other checks that read this repository's own source.
/// </summary>
public sealed partial class SettingsSectionTests
{
    private const string AppRoot = "../../../../../src/SysTuneX.App";

    private static string PagePath => Path.Combine(AppRoot, "ViewModels/SettingsViewModel.cs");

    private static string SectionsDirectory => Path.Combine(AppRoot, "ViewModels/Settings");

    /// <summary>
    /// A generated change handler and its body: either expression-bodied or braced. The closing
    /// brace is matched at method indentation, which is how the body's own blocks are skipped.
    /// </summary>
    [GeneratedRegex(@"partial void (On\w+Changed)\([^)]*\)\s*(?:=>(?<expression>[^;]*);|(?<block>\{[\s\S]*?\n    \}))")]
    private static partial Regex ChangeHandler { get; }

    /// <summary>A private helper the handler may delegate the decision to, and its body.</summary>
    [GeneratedRegex(@"private (?:void|async Task|Task) (\w+)\([^)]*\)\s*(?:=>(?<expression>[^;]*);|(?<block>\{[\s\S]*?\n    \}))")]
    private static partial Regex PrivateMethod { get; }

    /// <summary>A child section the page exposes, such as <c>public ScheduleSettingsViewModel Schedule</c>.</summary>
    [GeneratedRegex(@"public (\w+SettingsViewModel) (\w+) \{ get; \}")]
    private static partial Regex Section { get; }

    /// <summary>
    /// The flag is spelled three ways for good reasons - the page owns the field, a section reads it
    /// through the interface - so the check is about reaching it, not about matching one name.
    /// </summary>
    private static bool ConsultsTheFlag(string body) =>
        body.Contains("IsLoading", StringComparison.Ordinal) ||
        body.Contains("_isLoading", StringComparison.Ordinal);

    [Fact]
    public void Every_settings_change_handler_consults_the_loading_flag()
    {
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (string file in SettingsSources())
        {
            string text = File.ReadAllText(file);
            Dictionary<string, string> helpers = Helpers(text);

            foreach (Match handler in ChangeHandler.Matches(text))
            {
                string body = Body(handler);

                if (ConsultsTheFlag(body))
                {
                    continue;
                }

                // The schedule's three handlers are one line each and hand the decision to Apply().
                // Following one step is enough: a helper that delegates again is indirection this
                // check should reject rather than chase.
                bool delegated = helpers
                    .Where(pair => body.Contains(pair.Key + "(", StringComparison.Ordinal))
                    .Any(pair => ConsultsTheFlag(pair.Value));

                if (!delegated)
                {
                    offenders.Add($"{Path.GetFileName(file)}: {handler.Groups[1].Value}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_section_the_page_shows_is_loaded_when_the_page_opens()
    {
        string page = File.ReadAllText(PagePath);
        string opening = OnEnter(page);

        List<string> unloaded =
        [
            .. Section.Matches(page)
                .Select(match => match.Groups[2].Value)
                .Where(name => !opening.Contains($"{name}.Load", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(unloaded);
    }

    /// <summary>
    /// Guards both scans. A regex that matched no handlers, or no sections, would pass them
    /// trivially - and this file exists precisely because nobody would notice that.
    /// </summary>
    [Fact]
    public void The_scan_finds_the_sections_and_their_handlers()
    {
        List<string> files = [.. SettingsSources()];
        Assert.True(files.Count >= 6, $"Only {files.Count} settings source file(s) were found.");

        int handlers = files.Sum(file => ChangeHandler.Matches(File.ReadAllText(file)).Count);
        Assert.True(handlers >= 14, $"Only {handlers} change handler(s) were found.");

        string page = File.ReadAllText(PagePath);

        List<string> sections = [.. Section.Matches(page).Select(m => m.Groups[2].Value)];
        Assert.Contains("Schedule", sections);
        Assert.Contains("Compact", sections);
        Assert.True(sections.Count >= 5, $"Only {sections.Count} section(s) were found on the page.");

        Assert.Contains("Games.Load", OnEnter(page));

        // Both spellings of the flag have to be reachable, or the check below is half blind.
        Assert.True(ConsultsTheFlag("if (_isLoading)"));
        Assert.True(ConsultsTheFlag("if (_host.IsLoading)"));
    }

    /// <summary>The body of whichever form the regex matched.</summary>
    private static string Body(Match match) =>
        match.Groups["block"].Success ? match.Groups["block"].Value : match.Groups["expression"].Value;

    private static Dictionary<string, string> Helpers(string text)
    {
        var helpers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match method in PrivateMethod.Matches(text))
        {
            helpers[method.Groups[1].Value] = Body(method);
        }

        return helpers;
    }

    /// <summary>What the page does when it is opened, which is where the sections are loaded.</summary>
    private static string OnEnter(string page)
    {
        Match match = Regex.Match(
            page,
            @"protected override (?:async )?Task OnEnterAsync\(\)\s*\{[\s\S]*?\n    \}");

        Assert.True(match.Success, "The settings page no longer has an OnEnterAsync to read.");
        return match.Value;
    }

    private static IEnumerable<string> SettingsSources()
    {
        if (Directory.Exists(SectionsDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(SectionsDirectory, "*.cs"))
            {
                yield return file;
            }
        }

        if (File.Exists(PagePath))
        {
            yield return PagePath;
        }
    }
}
