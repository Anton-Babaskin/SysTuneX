using System.Reflection;
using System.Text.RegularExpressions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// A change to the machine is not the page's to cancel.
///
/// Every page hands out a token that it cuts when the user leaves, which is right for a scan and
/// was wrong for everything else it was given to. Every change in the application received it:
/// press Apply All and click away to the dashboard, and the batch stopped after whichever tweak it
/// had reached. A cancellation is not an error, so nothing said so. Restore All stopped the same way;
/// so did restarting Explorer, between ending it and starting it again; so did turning game mode on.
///
/// Which methods count as a change is read from Core rather than listed here: anything returning
/// one of the result types a change reports with. A new one is covered the day it is written.
/// Text in, text out, like the other checks that read this repository's own source.
/// </summary>
public sealed partial class MachineChangeTests
{
    private const string ViewModelRoot = "../../../../../src/SysTuneX.App/ViewModels";

    /// <summary>What a change reports with. A scan reports with its own type and is not one of these.</summary>
    private static readonly Type[] ChangeResults =
    [
        typeof(OperationResult),
        typeof(BatchResult),
        typeof(ProfileApplyResult),
        typeof(QuickOptimizeResult),
        typeof(MemoryTrimResult),
        typeof(GameModeResult),
        typeof(CleanupRunResult),
    ];

    /// <summary>Any identifier that is a token: PageToken, token, the busy scope's Token.</summary>
    [GeneratedRegex(@"\b\w*[Tt]oken\b")]
    private static partial Regex TokenIdentifier { get; }

    [GeneratedRegex(@"\bRunBusyAsync\(")]
    private static partial Regex ReadRunner { get; }

    [Fact]
    public void The_changes_are_found_rather_than_assumed()
    {
        HashSet<string> names = ChangeMethodNames();

        // If reflection stopped finding them, every test below would pass by checking nothing.
        Assert.Superset(
            new HashSet<string>(StringComparer.Ordinal)
            {
                "ApplyAsync", "RevertAsync", "ApplyManyAsync", "RevertManyAsync", "RestoreEverythingAsync",
                "EnableAsync", "DisableAsync", "SetDnsAsync", "CleanAsync", "RestartExplorerAsync", "RunAsync",
            },
            names);

        Assert.DoesNotContain("ScanAsync", names);

        Assert.True(
            ChangeCalls().Count() >= 20,
            "Too few change calls found in the view models; the pattern has stopped matching.");
    }

    [Fact]
    public void No_change_to_the_machine_is_handed_a_token_that_leaving_a_page_cancels()
    {
        List<string> violations =
        [
            .. ChangeCalls()
                .Where(call => TokenIdentifier.IsMatch(call.Arguments.Replace("CancellationToken.None", string.Empty, StringComparison.Ordinal)))
                .Select(call => $"{call.Where}  {call.Method}({Squash(call.Arguments)})"),
        ];

        Assert.True(
            violations.Count == 0,
            "These changes are handed a token the page cuts when the user leaves, so leaving stops them "
            + "halfway. Run them through RunChangeAsync or RunItemChangeAsync, which hand out none:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The other half. A change inside RunBusyAsync is handed nothing if its author left the token
    /// off, but it still bypasses the rule that one change runs at a time.
    /// </summary>
    [Fact]
    public void A_read_runner_never_carries_a_change()
    {
        Regex change = ChangeCallPattern();
        List<string> violations = [];

        foreach ((string path, string text) in ViewModelSources())
        {
            foreach (Match runner in ReadRunner.Matches(text))
            {
                string body = Arguments(text, runner.Index + runner.Length - 1);

                foreach (Match call in change.Matches(body))
                {
                    violations.Add($"{Where(path, text, runner.Index)}  RunBusyAsync carries {call.Groups[1].Value}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "RunBusyAsync is for reads; a change belongs in RunChangeAsync:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private sealed record ChangeCall(string Method, string Arguments, string Where);

    private static IEnumerable<ChangeCall> ChangeCalls()
    {
        Regex change = ChangeCallPattern();

        foreach ((string path, string text) in ViewModelSources())
        {
            foreach (Match call in change.Matches(text))
            {
                yield return new ChangeCall(
                    call.Groups[1].Value,
                    Arguments(text, call.Index + call.Length - 1),
                    Where(path, text, call.Index));
            }
        }
    }

    /// <summary>Every method on a Core interface that reports with a change result.</summary>
    private static HashSet<string> ChangeMethodNames() =>
    [
        .. typeof(ITweakEngine).Assembly
            .GetExportedTypes()
            .Where(type => type.IsInterface)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(method => method.ReturnType.IsGenericType &&
                             method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>) &&
                             ChangeResults.Contains(method.ReturnType.GetGenericArguments()[0]))
            .Select(method => method.Name),
    ];

    private static Regex ChangeCallPattern() =>
        new(@"\.(" + string.Join("|", ChangeMethodNames().Select(Regex.Escape)) + @")\(", RegexOptions.CultureInvariant);

    private static IEnumerable<(string Path, string Text)> ViewModelSources() =>
        Directory
            .EnumerateFiles(ViewModelRoot, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => (path, File.ReadAllText(path)));

    /// <summary>The text between the parenthesis at <paramref name="open"/> and the one that closes it.</summary>
    private static string Arguments(string text, int open)
    {
        int depth = 0;

        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return text[(open + 1)..i];
            }
        }

        return text[(open + 1)..];
    }

    private static string Where(string path, string text, int index) =>
        $"{Path.GetFileName(path)}:{text.AsSpan(0, index).Count('\n') + 1}";

    private static string Squash(string arguments) =>
        Regex.Replace(arguments, @"\s+", " ").Trim();
}
