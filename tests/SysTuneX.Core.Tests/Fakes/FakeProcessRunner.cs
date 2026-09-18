using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Services;

namespace SysTuneX.Core.Tests.Fakes;

/// <summary>
/// An external tool that prints whatever the test says it prints.
///
/// This is the whole point of <see cref="IProcessRunner"/> existing. Everything SysTuneX cannot do
/// through an API it does by running powercfg, netsh or bcdedit and reading the output - and the
/// reading is the part that goes wrong, because those tools print in the user's language and their
/// output shape is not a contract. While the runner was static, none of that could be reached: a
/// comment in PowerService said so in as many words.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(string Needle, ProcessRunResult Result)> _rules = [];

    /// <summary>Every run that was asked for, in order, so a test can check what was actually called.</summary>
    public List<(string FileName, string Arguments)> Calls { get; } = [];

    /// <summary>What an unmatched run returns. Success with no output, so a test only scripts what it cares about.</summary>
    public ProcessRunResult Fallback { get; set; } = ProcessRunResult.Ok();

    /// <summary>
    /// Answers <paramref name="result"/> when the file name or arguments contain
    /// <paramref name="needle"/>. Rules are tried in the order they were added.
    /// </summary>
    public FakeProcessRunner When(string needle, ProcessRunResult result)
    {
        _rules.Add((needle, result));
        return this;
    }

    /// <summary>Convenience for the common case: this command prints this text and succeeds.</summary>
    public FakeProcessRunner Printing(string needle, string output) => When(needle, ProcessRunResult.Ok(output));

    /// <summary>Convenience for the other common case: this command fails.</summary>
    public FakeProcessRunner Failing(string needle, string error = "failed") =>
        When(needle, ProcessRunResult.Failed(error));

    /// <summary>The arguments of every run whose command line mentions <paramref name="needle"/>.</summary>
    public IReadOnlyList<string> ArgumentsMatching(string needle) =>
        [.. Calls.Where(c => Line(c).Contains(needle, StringComparison.OrdinalIgnoreCase)).Select(c => c.Arguments)];

    public Task<ProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((fileName, arguments));

        string line = $"{fileName} {arguments}";
        foreach ((string needle, ProcessRunResult result) in _rules)
        {
            if (line.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(result);
            }
        }

        return Task.FromResult(Fallback);
    }

    public Task<ProcessRunResult> RunPowerShellAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        // The command itself rather than the base64 the real runner sends, so a test can match on
        // what was asked for rather than on an encoding.
        return RunAsync("powershell.exe", command, timeout, cancellationToken);
    }

    private static string Line((string FileName, string Arguments) call) => $"{call.FileName} {call.Arguments}";
}
