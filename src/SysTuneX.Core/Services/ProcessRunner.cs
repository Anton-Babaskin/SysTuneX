using System.Diagnostics;
using System.Text;

namespace SysTuneX.Core.Services;

/// <param name="ExitCode">-1 when the process could not be started or was cancelled.</param>
public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    /// <summary>Whichever stream carries the useful text, preferring stderr when the run failed.</summary>
    public string Output => Success || string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
}

/// <summary>
/// Runs the Windows console tools SysTuneX depends on (powercfg, netsh, bcdedit and friends)
/// with output captured. The previous code fired these off and ignored both the exit code and
/// the error text, which is why failures looked like silence in the UI.
/// </summary>
public static class ProcessRunner
{
    public static async Task<ProcessRunResult> RunAsync(
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

    /// <summary>Runs a PowerShell command with the profile skipped so a user profile cannot break parsing.</summary>
    public static Task<ProcessRunResult> RunPowerShellAsync(
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
