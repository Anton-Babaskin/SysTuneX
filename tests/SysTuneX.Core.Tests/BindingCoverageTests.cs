using System.Text.RegularExpressions;
using System.Xml.Linq;
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
/// There are two checks here, and they catch different mistakes.
///
/// The first is deliberately loose about *where* a property lives. A binding inside a
/// DataTemplate resolves against the item, not the page's view model, and working out which item
/// type a template will be given is not something a text scan can do honestly. Requiring the name
/// to exist *somewhere* in the source still catches the mistake that actually happens - a typo or
/// a rename - without inventing certainty about the rest.
///
/// The second is strict about where, for the bindings that are not in a template. It exists
/// because the pages started scoping parts of themselves to child view models with
/// <c>DataContext="{Binding Snapshots}"</c>: under such an element a name that is still perfectly
/// real on the page's own view model now resolves against nothing, which is precisely the
/// leftover a property moved between the two classes leaves behind.
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
        Assert.Contains("CpuHistory", declared);        // a plain property
        Assert.Contains("CpuTopology", declared);       // an expression-bodied one
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

    // ---- The second check: bindings outside a template resolve against the right view model ----

    /// <summary>The page's view model, from <c>d:DataContext="{d:DesignInstance Type=vm:Foo}"</c>.</summary>
    [GeneratedRegex(@"d:DesignInstance\s+(?:Type=)?(?:vm:)?(\w+)")]
    private static partial Regex DesignInstance { get; }

    /// <summary>A binding's whole path: <c>Counters.CpuUsage</c>, not just <c>Counters</c>.</summary>
    [GeneratedRegex(@"\{Binding\s+(?:Path=)?([A-Za-z_][\w.]*)")]
    private static partial Regex FullBindingPath { get; }

    /// <summary>
    /// Every binding a page makes against its own view model - or against a child view model it
    /// scoped part of itself to - names something that view model actually has.
    /// </summary>
    [Fact]
    public void Every_binding_outside_a_template_names_a_member_of_its_own_view_model()
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);

        foreach ((string file, XDocument document, string viewModel) in Views())
        {
            string name = Path.GetFileName(file);

            Check(document.Root!, viewModel, name, missing, outer: viewModel);
            Walk(document.Root!, viewModel, name, missing);
        }

        Assert.Empty(missing);
    }

    /// <summary>
    /// Guards the scan above. Harvesting no members would make every binding look broken; finding
    /// no pages, or no scoped ones, would make every page look clean - and the scoped ones are the
    /// reason this second check exists at all.
    /// </summary>
    [Fact]
    public void The_scoped_scan_finds_the_pages_their_view_models_and_the_scopes()
    {
        List<(string File, XDocument Document, string ViewModel)> views = [.. Views()];

        Assert.True(views.Count >= 6, $"Only {views.Count} views declared a view model.");
        Assert.Contains(views, v => v.ViewModel == "HistoryViewModel");

        HashSet<string> history = Members("HistoryViewModel");
        Assert.Contains("Entries", history);
        Assert.Contains("ActiveCount", history);          // [ObservableProperty] private int _activeCount
        Assert.Contains("RevertAllCommand", history);     // [RelayCommand] private async Task RevertAllAsync
        Assert.Contains("Snapshots", history);
        Assert.Contains("IsBusy", history);               // inherited from PageViewModel

        // Four of the pages declare almost nothing and take their content from a shared base class,
        // so a harvest that stopped at the class itself would report all of them as broken.
        Assert.Contains("Groups", Members("NetworkViewModel"));

        // The construct this second check is about: a subtree rebound to a child view model.
        int scopes = views.Sum(v => v.Document.Descendants()
            .Count(e => (string?)e.Attribute("DataContext") is { Length: > 0 } value &&
                        value.StartsWith("{Binding", StringComparison.Ordinal)));

        Assert.True(scopes >= 1, "No page scopes a subtree to a child view model any more.");
        Assert.Contains("CaptureSnapshotCommand", Members("SnapshotsViewModel"));
    }

    /// <summary>
    /// Walks a subtree bound to <paramref name="context"/>, following any element that rebinds
    /// itself to a child view model and stopping at templates, whose data context is an item
    /// rather than a view model.
    /// </summary>
    private static void Walk(XElement element, string context, string file, SortedSet<string> missing)
    {
        foreach (XElement child in element.Elements())
        {
            // A DataTemplate's content is bound to whatever is in the collection, and so is an
            // ItemsControl's ItemTemplate. Neither is resolvable from here - that is what the
            // loose check above is for.
            if (child.Name.LocalName.Contains("Template", StringComparison.Ordinal))
            {
                continue;
            }

            string scoped = context;

            if ((string?)child.Attribute("DataContext") is { } data &&
                BindingPath.Match(data) is { Success: true } match)
            {
                // This element and everything under it read a different object. Which object is a
                // question about C# types, not markup, so the property's declared type is it.
                scoped = PropertyType(context, match.Groups[1].Value) ?? context;
            }

            Check(child, scoped, file, missing, outer: context);
            Walk(child, scoped, file, missing);
        }
    }

    private static void Check(
        XElement element,
        string viewModel,
        string file,
        SortedSet<string> missing,
        string outer)
    {
        HashSet<string> members = Members(viewModel);

        if (members.Count == 0)
        {
            return;
        }

        foreach (XAttribute attribute in element.Attributes())
        {
            string value = attribute.Value;

            // Anything naming its own source resolves somewhere this scan cannot follow.
            if (value.Contains("RelativeSource", StringComparison.Ordinal) ||
                value.Contains("ElementName", StringComparison.Ordinal) ||
                value.Contains("Source=", StringComparison.Ordinal))
            {
                continue;
            }

            // The attribute that set this scope up was itself written against the enclosing one.
            bool isScope = attribute.Name.LocalName == "DataContext";
            string start = isScope ? outer : viewModel;

            foreach (Match match in FullBindingPath.Matches(value))
            {
                Resolve(match.Groups[1].Value, start, file, missing);
            }
        }
    }

    /// <summary>
    /// Follows <c>Counters.CpuUsage</c> one step at a time: the first name has to be on the view
    /// model the element reads, and the second on whatever type that property is.
    ///
    /// Following the whole path rather than only its first step is the difference between the
    /// dashboard's bindings being checked and merely looking checked. Its three cards interleave
    /// on screen, so they are reached by path rather than by a scoped subtree, and a scan that
    /// stopped at "Counters" would have verified nothing about the thirty-odd names after the dot.
    /// </summary>
    private static void Resolve(string path, string viewModel, string file, SortedSet<string> missing)
    {
        string? type = viewModel;

        foreach (string step in path.Split('.'))
        {
            if (ProvidedByWpf.Contains(step))
            {
                return;
            }

            // The type is not one of ours - a collection's Count, a TimeSpan's Minutes. There is
            // nothing dishonest to say about the rest of the path, so this stops rather than guesses.
            if (type is null || Members(type).Count == 0)
            {
                return;
            }

            if (!Members(type).Contains(step))
            {
                missing.Add($"{step} (bound in {file}, expected on {type})");
                return;
            }

            type = PropertyType(type, step);
        }
    }

    /// <summary>The declared type of a property, so a scoped subtree knows what it is reading.</summary>
    private static string? PropertyType(string viewModel, string property)
    {
        if (Source(viewModel) is not { } text)
        {
            return null;
        }

        Match match = Regex.Match(text, $@"public\s+([\w<>,?\.]+)\s+{Regex.Escape(property)}\s*(?:\{{|=>)");

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Everything a binding can legally name on one view model: its own members, the ones
    /// CommunityToolkit generates from its fields and methods, and all of the same from whatever
    /// it inherits.
    /// </summary>
    private static HashSet<string> Members(string viewModel)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        string? type = viewModel;

        // The bound depth guards a cycle, which C# cannot have but a regex misreading one can.
        for (int depth = 0; type is not null && depth < 8; depth++)
        {
            if (Source(type) is not { } text)
            {
                break;
            }

            foreach (Match match in DeclaredMember.Matches(text))
            {
                members.Add(match.Groups[1].Value);
            }

            foreach (Match match in GeneratedProperty.Matches(text))
            {
                string field = match.Groups[1].Value;
                members.Add(char.ToUpperInvariant(field[0]) + field[1..]);
            }

            foreach (Match match in GeneratedCommand.Matches(text))
            {
                string method = match.Groups[1].Value;
                members.Add(method.EndsWith("Async", StringComparison.Ordinal)
                    ? method[..^5] + "Command"
                    : method + "Command");
            }

            type = BaseType(text, type);
        }

        return members;
    }

    /// <summary>The class a view model extends, when that class is one of ours.</summary>
    private static string? BaseType(string text, string type)
    {
        Match declaration = Regex.Match(text, $@"class\s+{Regex.Escape(type)}\s*:\s*([\w<>,\s\.]+)");

        return declaration.Success
            ? declaration.Groups[1].Value
                .Split(',')
                .Select(part => part.Trim())
                .FirstOrDefault(part => Source(part) is not null)
            : null;
    }

    private static readonly Dictionary<string, string?> SourceCache = new(StringComparer.Ordinal);

    private static string? Source(string type)
    {
        if (SourceCache.TryGetValue(type, out string? cached))
        {
            return cached;
        }

        string[] found = Directory.Exists(AppRoot)
            ? Directory.GetFiles(AppRoot, $"{type}.cs", SearchOption.AllDirectories)
            : [];

        // More than one file of that name means the guess about which class this is, is a guess.
        string? text = found.Length == 1 ? File.ReadAllText(found[0]) : null;
        SourceCache[type] = text;
        return text;
    }

    private static IEnumerable<(string File, XDocument Document, string ViewModel)> Views()
    {
        foreach ((string file, string text) in Files(AppRoot, ".xaml"))
        {
            if (DesignInstance.Match(text) is not { Success: true } declared)
            {
                continue;
            }

            XDocument document;

            try
            {
                document = XDocument.Parse(text);
            }
            catch (System.Xml.XmlException)
            {
                continue;   // ResourceCoverageTests is the one that reports this.
            }

            yield return (file, document, declared.Groups[1].Value);
        }
    }
}
