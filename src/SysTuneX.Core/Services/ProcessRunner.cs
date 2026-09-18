using System.Diagnostics;
using System.Text;
using SysTuneX.Core.Abstractions;

namespace SysTuneX.Core.Services;

/// <param name="ExitCode">-1 when the process could not be started or was cancelled.</param>
public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    /// <summary>Whichever stream carries the useful text, preferring stderr when the run failed.</summary>
    public string Output => Success || string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;

    /// <summary>A run that succeeded and printed <paramref name="standardOutput"/>. For tests.</summary>
    public static ProcessRunResult Ok(string standardOutput = "") => new(0, standardOutput, string.Empty);

    /// <summary>A run that failed. For tests.</summary>
    public static ProcessRunResult Failed(string standardError = "failed") => new(-1, string.Empty, standardError);
}

/// <summary>
/// Runs the Windows console tools SysTuneX depends on - powercfg, netsh, bcdedit, PowerShell.
///
/// An interface rather than a static class, and that is not ceremony. Every service that reads
/// one of these tools has to parse its output, parsing is the part that goes wrong, and while the
/// runner was static none of it could be reached from a test. That is not a hypothetical: the
/// powercfg parser matched on a translated word and had never been run against either language it
/// claimed to handle, and the frame counter compared two clocks for the same reason - nobody could
/// see it. Both were found by reading, not by testing, which is the expensive way.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(-1, string.Empty, $"Could not start {fileName}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));

            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return new ProcessRunResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new ProcessRunResult(-1, stdout.ToString(), $"{fileName} timed out.");
        }
        catch (Exception ex)
        {
            TryKill(process);
            return new ProcessRunResult(-1, stdout.ToString(), ex.Message);
        }
    }

    /// <summary>
    /// Runs a PowerShell command with the profile skipped so a user profile cannot break parsing.
    ///
    /// Base64 rather than quoting. A command passed as text has to survive both the Windows command
    /// line and PowerShell's own parser, and every quoting scheme anyone writes for that is a bug
    /// waiting for a path with an apostrophe in it. EncodedCommand has no quoting to get wrong.
    /// </summary>
    public Task<ProcessRunResult> RunPowerShellAsync(
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        return RunAsync(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            timeout ?? TimeSpan.FromSeconds(60),
            cancellationToken);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process is already gone, which is the outcome we wanted.
        }
    }
}
