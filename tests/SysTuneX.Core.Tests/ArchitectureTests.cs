using System.Text.RegularExpressions;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Rules about the shape of the code that no compiler enforces and that a review will eventually
/// miss. Each one here is a mistake this project actually made.
/// </summary>
public sealed partial class ArchitectureTests
{
    private const string CoreRoot = "../../../../../src/SysTuneX.Core";
    private const string AppRoot = "../../../../../src/SysTuneX.App";

    /// <summary>The two static members that read a fixed path off the machine.</summary>
    [GeneratedRegex(@"AppPaths\.(DataDirectory|LogDirectory)\b")]
    private static partial Regex StaticPath { get; }

    /// <summary>
    /// A service reading a static path cannot be pointed anywhere else.
    ///
    /// That is not tidiness. Half the services took an optional <c>dataDirectory</c> and fell back
    /// to the static one; no test ever passed the parameter, so every test of the profile,
    /// snapshot, game-mode and game-watcher services read and wrote the real
    /// <c>%ProgramData%\SysTuneX</c> on whatever machine ran them - leaving state behind between
    /// runs and reading state the developer's own installation had put there.
    ///
    /// The path comes from <c>IEnvironmentService</c> now. The two exceptions below are the logger,
    /// which has to write before the container exists, and the class that defines the paths.
    /// </summary>
    [Fact]
    public void Only_the_logger_reads_a_static_data_directory()
    {
        string[] allowed =
        [
            "AppPaths.cs",              // defines them
            "FileLoggerProvider.cs",    // starts before there is anything to inject
            "FileLogger.cs",
        ];

        List<string> offenders =
        [
            .. Sources()
                .Where(pair => !allowed.Contains(Path.GetFileName(pair.File), StringComparer.Ordinal))
                .Where(pair => StaticPath.IsMatch(pair.Text))
                .Select(pair => Path.GetFileName(pair.File))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Guards the scan above: if it stopped finding the file that legitimately uses the static
    /// path, it would also have stopped finding anything that misused it.
    /// </summary>
    [Fact]
    public void The_static_path_scan_still_finds_the_logger()
    {
        Assert.Contains(
            Sources(),
            pair => Path.GetFileName(pair.File) == "FileLoggerProvider.cs" && StaticPath.IsMatch(pair.Text));
    }

    /// <summary>
    /// Running an external tool has to go through the injected runner.
    ///
    /// While it was static, nothing that parsed powercfg, netsh or bcdedit output could be reached
    /// from a test - and parsing is the part that goes wrong, because those tools print in the
    /// user's language. Two real defects came out of exactly that blind spot: a power setting
    /// reader that treated "could not read" as zero, and a frame counter comparing two clocks.
    /// </summary>
    [Fact]
    public void Nothing_calls_the_process_runner_statically()
    {
        List<string> offenders =
        [
            .. Sources()
                .Where(pair => Path.GetFileName(pair.File) != "ProcessRunner.cs")
                .Where(pair => pair.Text.Contains("ProcessRunner.Run", StringComparison.Ordinal))
                .Select(pair => Path.GetFileName(pair.File))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(offenders);
    }

    /// <summary>
    /// A view model is a description of a screen. Starting a process from one puts an action the
    /// user cannot undo - <c>shutdown /r</c> was the real example - in the layer furthest from
    /// anything that could test it.
    /// </summary>
    [Fact]
    public void No_view_model_starts_a_process()
    {
        List<string> offenders =
        [
            .. Sources(AppRoot)
                .Where(pair => pair.File.Contains("ViewModels", StringComparison.Ordinal))
                .Where(pair => pair.Text.Contains("Process.Start", StringComparison.Ordinal) ||
                               pair.Text.Contains("ProcessRunner", StringComparison.Ordinal))
                .Select(pair => Path.GetFileName(pair.File))
                .Order(StringComparer.Ordinal),
        ];

        Assert.Empty(offenders);
    }

    private static IReadOnlyList<(string File, string Text)> Sources(string? root = null)
    {
        var files = new List<(string, string)>();

        foreach (string directory in root is null ? (string[])[CoreRoot, AppRoot] : [root])
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                files.Add((file, File.ReadAllText(file)));
            }
        }

        return files;
    }
}
