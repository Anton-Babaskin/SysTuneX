using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Values reach PowerShell as arguments, never as script text.
///
/// The scheduled task and restore point services used to quote values into their scripts, doubling
/// the ASCII apostrophe on the way. PowerShell also reads U+2018 to U+201B as single quotes, so a
/// task path containing ’ ended the string, and the rest of the path ran as a statement of its own -
/// as administrator, from a path that can come back out of the change journal.
/// </summary>
public sealed partial class PowerShellArgumentTests
{
    /// <summary>Every character PowerShell accepts as a single quote.</summary>
    private static readonly char[] Quotes = ['\'', '\u2018', '\u2019', '\u201A', '\u201B'];

    [GeneratedRegex(@"FromBase64String\('([A-Za-z0-9+/=]*)'\)")]
    private static partial Regex EncodedArgument { get; }

    /// <summary>A value that closes the string, runs a command, and opens a new string so the rest still parses.</summary>
    private static string Breakout(char quote) => $@"\Microsoft\Windows\Test{quote}; Remove-Item C:\Windows -Recurse; {quote}";

    /// <summary>The values a command hands its script, decoded the way PowerShell decodes them.</summary>
    private static List<string> ArgumentsOf(string command) =>
    [
        .. EncodedArgument
            .Matches(command)
            .Select(match => Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups[1].Value))),
    ];

    [Theory]
    [InlineData('\'')]
    [InlineData('\u2018')]
    [InlineData('\u2019')]
    [InlineData('\u201A')]
    [InlineData('\u201B')]
    public async Task A_task_path_with_a_quote_in_it_reaches_PowerShell_as_an_argument(char quote)
    {
        var runner = new FakeProcessRunner();
        var tasks = new ScheduledTaskService(NullLogger<ScheduledTaskService>.Instance, new FakeEnvironment(), runner);
        string path = Breakout(quote);

        await tasks.GetStateAsync([path]);
        await tasks.SetEnabledAsync([path], enabled: true);

        Assert.Equal(2, runner.Calls.Count);
        Assert.All(runner.Calls, call =>
        {
            Assert.DoesNotContain("Remove-Item", call.Arguments, StringComparison.Ordinal);
            Assert.Equal([path], ArgumentsOf(call.Arguments));
        });
    }

    [Fact]
    public async Task Several_task_paths_arrive_as_several_arguments_in_order()
    {
        var runner = new FakeProcessRunner();
        var tasks = new ScheduledTaskService(NullLogger<ScheduledTaskService>.Instance, new FakeEnvironment(), runner);
        string[] paths = [.. Quotes.Select(Breakout)];

        await tasks.SetEnabledAsync(paths, enabled: false);

        Assert.Equal(paths, ArgumentsOf(Assert.Single(runner.Calls).Arguments));
    }

    [Theory]
    [InlineData('\'')]
    [InlineData('\u2018')]
    [InlineData('\u2019')]
    [InlineData('\u201A')]
    [InlineData('\u201B')]
    public async Task A_restore_point_description_with_a_quote_in_it_reaches_PowerShell_as_an_argument(char quote)
    {
        var runner = new FakeProcessRunner();
        var restorePoints = new RestorePointService(
            NullLogger<RestorePointService>.Instance,
            new FakeRegistryService(),
            new FakeEnvironment(),
            runner);

        string description = $"Before SysTuneX{quote}; Remove-Item C:\\Windows -Recurse; {quote}";

        await restorePoints.CreateAsync(description);

        string command = Assert.Single(runner.Calls).Arguments;
        Assert.DoesNotContain("Remove-Item", command, StringComparison.Ordinal);
        Assert.Equal([description], ArgumentsOf(command));
    }

    /// <summary>
    /// The package name was quoted into its script the same way, behind a filter that refused the
    /// ASCII apostrophe and none of the other four.
    /// </summary>
    [Theory]
    [InlineData('\u2018')]
    [InlineData('\u2019')]
    [InlineData('\u201A')]
    [InlineData('\u201B')]
    public async Task A_package_name_with_a_typographic_quote_reaches_PowerShell_as_an_argument(char quote)
    {
        var runner = new FakeProcessRunner();
        var packages = new AppPackageService(NullLogger<AppPackageService>.Instance, runner);

        string name = $"Microsoft.BingNews{quote}) Remove-Item C:\\Windows -Recurse #";

        await packages.RemoveAppAsync(name);

        string command = Assert.Single(runner.Calls).Arguments;
        Assert.DoesNotContain("Remove-Item", command, StringComparison.Ordinal);
        Assert.Equal([name], ArgumentsOf(command));
    }

    /// <summary>
    /// The encoding only helps if PowerShell decodes it back to the same text, so this asks the real
    /// one: every quote, the characters that start a subexpression or an escape, an empty string and
    /// a line break all have to come out exactly as they went in.
    /// </summary>
    [Fact]
    public async Task PowerShell_receives_every_value_exactly_as_it_was_passed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string[] values =
        [
            .. Quotes.Select(quote => $"a{quote}b"),
            "\"double\" \u201Csmart\u201D",
            "$(Get-Date) `n $env:TEMP",
            "Привет, мир",
            string.Empty,
            "first line\nsecond line",
        ];

        // Each value comes back as base64 behind a marker, so neither the console's code page nor
        // an empty line can change what the test reads.
        ProcessRunResult result = await new ProcessRunner().RunPowerShellAsync(
            PowerShellScript.WithArguments(
                "$args | ForEach-Object { '>' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($_)) }",
                values));

        Assert.True(result.Success, result.Output);

        List<string> received =
        [
            .. result.StandardOutput
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith('>'))
                .Select(line => Encoding.UTF8.GetString(Convert.FromBase64String(line[1..]))),
        ];

        Assert.Equal(values, received);
    }

    /// <summary>
    /// The defect itself, against the real PowerShell: a task path built to close its string and
    /// create a file. Before the fix, the paths with a typographic quote in them created it.
    /// </summary>
    [Fact]
    public async Task A_quote_in_a_task_path_does_not_run_what_follows_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string temp = Environment.GetEnvironmentVariable("TEMP") ?? Path.GetTempPath();
        string marker = "systunex-injection-" + Guid.NewGuid().ToString("N");

        // Shaped for the script the paths used to be quoted into, @('path', ...), so that it still
        // parsed once the string had been closed early. No ASCII quote anywhere but the one tested.
        string[] paths =
        [
            .. Quotes.Select(quote =>
                $@"\SysTuneX{quote}) | Out-Null; New-Item -ItemType File -Path (Join-Path $env:TEMP {marker}) | Out-Null; @({quote}"),
        ];

        var tasks = new ScheduledTaskService(NullLogger<ScheduledTaskService>.Instance, new FakeEnvironment(), new ProcessRunner());

        try
        {
            await tasks.GetStateAsync(paths);

            Assert.False(File.Exists(Path.Combine(temp, marker)), "A task path ran as a PowerShell command.");
        }
        finally
        {
            File.Delete(Path.Combine(temp, marker));
        }
    }
}
