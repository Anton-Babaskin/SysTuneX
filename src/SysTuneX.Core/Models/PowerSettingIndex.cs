using System.Globalization;
using System.Text.RegularExpressions;

namespace SysTuneX.Core.Models;

/// <summary>
/// Reads the value out of what <c>powercfg /q</c> prints.
///
/// Extracted from <c>PowerService</c> because it is the part that can be wrong: powercfg prints in
/// the system's language, and the line we want is found by looking for a word in it. That test -
/// does this line say "index"? - was written against English and Russian and had never been run
/// against either, because reaching it needed powercfg on a Windows machine. It is plain text
/// parsing, so it belongs where a test can reach it.
///
/// Typical output, with the line we want last:
///
/// <code>
/// Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
///   Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
///     Power Setting GUID: 0cc5b647-c1df-4637-891a-dec35c318583  (Processor performance core parking min cores)
///       Current AC Power Setting Index: 0x00000064
///       Current DC Power Setting Index: 0x00000005
/// </code>
/// </summary>
public static partial class PowerSettingIndex
{
    /// <summary>
    /// Words that mark the line carrying a setting's current value, one per language powercfg is
    /// known to print in. English and Russian are what this project ships.
    /// </summary>
    private static readonly string[] IndexWords = ["index", "индекс"];

    /// <summary>Which of the two values a query returns - the machine on mains, or on battery.</summary>
    public enum Source
    {
        /// <summary>Plugged in. The one that matters for a desktop and for a laptop being played on.</summary>
        AC,

        /// <summary>On battery.</summary>
        DC,
    }

    [GeneratedRegex(@"0x(?<value>[0-9a-fA-F]{8})", RegexOptions.ExplicitCapture)]
    private static partial Regex HexValue { get; }

    /// <summary>
    /// The setting's current value, or null when the output does not carry one.
    ///
    /// Null rather than zero, and this matters: powercfg prints nothing useful when the setting is
    /// hidden on this machine, and a zero there would read as a real value. Several of these
    /// settings treat zero as "off", so guessing would report a machine as tuned when the setting
    /// does not exist on it at all.
    /// </summary>
    public static int? Parse(string? output, Source source = Source.AC)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var matches = new List<int>(2);

        foreach (string line in output.Split('\n'))
        {
            if (!IndexWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            Match match = HexValue.Match(line);
            if (match.Success && int.TryParse(
                    match.Groups["value"].Value,
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out int value))
            {
                matches.Add(value);
            }
        }

        // powercfg prints AC first, then DC. Taken by position rather than by looking for "AC" in
        // the text, because that abbreviation is itself translated.
        int wanted = source == Source.AC ? 0 : 1;
        return matches.Count > wanted ? matches[wanted] : null;
    }
}
