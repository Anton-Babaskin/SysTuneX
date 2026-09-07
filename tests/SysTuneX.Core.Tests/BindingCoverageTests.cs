using System.Text.RegularExpressions;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// A binding to a property that does not exist fails silently.
///
/// WPF writes a line to the debug output and renders nothing - no exception, no build warning,
/// no failing test. A renamed property leaves an empty tile behind it, and the only way to find
/// out is to open the page and notice. This project has already paid for that once: the 2.8.0
/// rewrite moved most of the monitor page's bindings and they had to be checked by hand, one at
/// a time, because the localization keys had a test and the binding paths had nothing.
///
/// The check is deliberately loose about *where* a property lives. A binding inside a
/// DataTemplate resolves against the item, not the page's view model, and working out which item
/// type a template will be given is not something a text scan can do honestly. Requiring the name
/// to exist *somewhere* in the source still catches the mistake that actually happens - a typo or
/// a rename - without inventing certainty about the rest.
/// </summary>
public sealed partial class BindingCoverageTests
{
    private const string AppRoot = "../../../../../src/SysTuneX.App";
    private const string SourceRoot = "../../../../../src";

    /// <summary>The property name a binding starts with: <c>{Binding Foo.Bar}</c> gives Foo.</summary>
    [GeneratedRegex(@"\{Binding\s+(?:Path=)?([A-Za-z_]\w*)")]
    private static partial Regex BindingPath { get; }

    /// <summary>A declared property or expression-bodied member.</summary>
    [GeneratedRegex(
        @"\b(?:public|internal|protected)\s+(?:static\s+|virtual\s+|override\s+|sealed\s+|required\s+|partial\s+)*[\w<>,\?\[\]\. ]+?\s+(\w+)\s*(?:\{|=>)")]
    private static partial Regex DeclaredMember { get; }

    /// <summary><c>[ObservableProperty] private int _cpuUsage;</c> generates <c>CpuUsage</c>.</summary>
    [GeneratedRegex(@"\[ObservableProperty\][^;]*?\b_(\w+)\s*(?:=|;)", RegexOptions.Singleline)]
    private static partial Regex GeneratedProperty { get; }

    /// <summary><c>[RelayCommand] private Task RefreshAsync()</c> generates <c>RefreshCommand</c>.</summary>
    [GeneratedRegex(@"\[RelayCommand[^\]]*\][^{;]*?\b(\w+)\s*\(", RegexOptions.Singleline)]
    private static partial Regex GeneratedCommand { get; }

    /// <summary>Positional record members: <c>record Row(SymbolRegular Icon, string Text)</c>.</summary>
    [GeneratedRegex(@"record\s+\w+\s*\(([^)]*)\)", RegexOptions.Singleline)]
    private static partial Regex RecordParameters { get; }

    /// <summary>
    /// Names WPF supplies itself, which no view model declares. <c>DataContext</c> is the one the
    /// app uses, to reach a page's view model from inside a template.
    /// </summary>
    private static readonly HashSet<string> ProvidedByWpf = new(StringComparer.Ordinal)
    {
        "DataContext",
    };

    [Fact]
    public void Every_binding_names_a_property_that_exists()
    {
        HashSet<string> declared = DeclaredNames();

        var missing = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach ((string file, string text) in Files(AppRoot, ".xaml"))
        {
            foreach (Match match in BindingPath.Matches(text))
            {
                string name = match.Groups[1].Value;

                if (!ProvidedByWpf.Contains(name) && !declared.Contains(name))
                {
                    missing[name] = Path.GetFileName(file);
                }
            }
        }

        Assert.Empty(missing.Select(pair => $"{pair.Key} (bound in {pair.Value})"));
    }

    /// <summary>
    /// Guards the scan above. If the property harvest silently collected nothing - a regex broken
    /// by a language feature, a moved directory - every binding would resolve against an empty set
    /// and the test would report every name in the app, or, worse, none at all.
    /// </summary>
    [Fact]
    public void The_property_scan_finds_what_it_should()
    {
        HashSet<string> declared = DeclaredNames();

        Assert.Contains("CpuUsage", declared);          // [ObservableProperty] private double _cpuUsage
        Assert.Contains("GameModeChanges", declared);   // a plain property
        Assert.Contains("RefreshCommand", declared);    // [RelayCommand] private Task RefreshAsync()
        Assert.Contains("Restores", declared);          // a positional record member
        Assert.True(declared.Count > 300, $"Only {declared.Count} property names were found.");
    }

    private static HashSet<string> DeclaredNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach ((_, string text) in Files(SourceRoot, ".cs"))
        {
            foreach (Match match in DeclaredMember.Matches(text))
            {
                names.Add(match.Groups[1].Value);
            }

            foreach (Match match in GeneratedProperty.Matches(text))
            {
                string field = match.Groups[1].Value;
                names.Add(char.ToUpperInvariant(field[0]) + field[1..]);
            }

            foreach (Match match in GeneratedCommand.Matches(text))
            {
                string method = match.Groups[1].Value;
                names.Add(method.EndsWith("Async", StringComparison.Ordinal) ? method[..^5] + "Command" : method + "Command");
            }

            foreach (Match match in RecordParameters.Matches(text))
            {
                foreach (string parameter in match.Groups[1].Value.Split(','))
                {
                    string[] words = parameter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length >= 2)
                    {
                        names.Add(words[^1]);
                    }
                }
            }
        }

        return names;
    }

    private static IEnumerable<(string File, string Text)> Files(string root, string extension)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*" + extension, SearchOption.AllDirectories))
        {
            // obj and bin carry generated copies of the same markup and source.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (file, File.ReadAllText(file));
        }
    }
}
