using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Every switch that runs a command shows what is true rather than what was clicked.
///
/// A toggle flips on the click, before its command runs. Bound one way, it only moves back when the
/// property it shows changes - so a declined confirmation or a refused write left it showing a tweak
/// as on that was off, or a service as running that was still disabled. Bound two way it is worse:
/// the click writes the wished-for state into the view model before anything has happened.
///
/// The behaviour that fixes it has to be asked for on each switch, which is exactly the kind of
/// thing the next switch forgets. Text in, text out, like the other checks that read this
/// repository's own markup.
/// </summary>
public sealed class SwitchStateCoverageTests
{
    private const string AppRoot = "../../../../../src/SysTuneX.App";

    private static readonly HashSet<string> Toggles = new(StringComparer.Ordinal)
    {
        "ToggleSwitch", "CheckBox", "ToggleButton", "RadioButton",
    };

    [Fact]
    public void Every_switch_that_runs_a_command_follows_its_source()
    {
        List<string> violations = [];
        int found = 0;

        foreach ((string path, XElement toggle) in CommandToggles())
        {
            found++;

            string isChecked = toggle.Attribute("IsChecked")?.Value ?? string.Empty;
            string where = $"{Path.GetFileName(path)}:{((IXmlLineInfo)toggle).LineNumber}";

            if (isChecked.StartsWith("{Binding", StringComparison.Ordinal) &&
                !isChecked.Contains("Mode=OneWay", StringComparison.Ordinal))
            {
                violations.Add($"{where}  {isChecked} writes the click into the view model before the command has run; bind it Mode=OneWay");
            }

            bool follows = toggle.Attributes().Any(attribute =>
                attribute.Name.LocalName == "SwitchState.FollowsSource" &&
                attribute.Value == "True");

            if (!follows)
            {
                violations.Add($"{where}  runs a command without controls:SwitchState.FollowsSource=\"True\"");
            }
        }

        // Five today: tweaks, services, the hosts block, game mode and the watched games. Fewer found
        // means the search has stopped matching, and the check above would pass by looking at nothing.
        Assert.True(found >= 5, $"Only {found} command switches found; the markup search has stopped matching.");

        Assert.True(
            violations.Count == 0,
            "A switch that runs a command must show the state it is bound to, not the click:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<(string Path, XElement Toggle)> CommandToggles() =>
        Directory
            .EnumerateFiles(AppRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .SelectMany(path => XDocument
                .Load(path, LoadOptions.SetLineInfo)
                .Descendants()
                .Where(element => Toggles.Contains(element.Name.LocalName) && element.Attribute("Command") is not null)
                .Select(element => (path, element)));
}
