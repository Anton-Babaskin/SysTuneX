using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IScheduledTaskService"/>
[SupportedOSPlatform("windows")]
public sealed class ScheduledTaskService : IScheduledTaskService
{
    /// <summary>
    /// PowerShell costs the best part of a second to start, so every task is asked about in one
    /// call rather than one call each. Twenty seconds is room for a slow first start.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Splits the full path into the folder and the name PowerShell wants.
    ///
    /// The trailing backslash is not cosmetic: Get-ScheduledTask with -TaskPath "\Microsoft\Windows\Autochk"
    /// finds nothing, while the same path ending in a backslash finds the folder - and
    /// Split-Path -Parent strips it.
    /// </summary>
    private const string SplitPath =
        "$p = Split-Path $_ -Parent; if (-not $p.EndsWith('\\')) { $p = $p + '\\' }; $n = Split-Path $_ -Leaf; ";

    private readonly ILogger<ScheduledTaskService> _logger;
    private readonly IEnvironmentService _environment;
    private readonly IProcessRunner _processes;

    public ScheduledTaskService(
        ILogger<ScheduledTaskService> logger,
        IEnvironmentService environment,
        IProcessRunner processes)
    {
        _logger = logger;
        _environment = environment;
        _processes = processes;
    }

    public async Task<IReadOnlyList<ScheduledTaskInfo>> GetStateAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
        {
            return [];
        }

        // -ErrorAction SilentlyContinue, because a task that does not exist on this build is a
        // perfectly ordinary answer - Windows editions differ - and should leave the other tasks
        // readable rather than turning the whole query into an error.
        string script =
            List(paths) + " | ForEach-Object { " + SplitPath +
            "$t = Get-ScheduledTask -TaskPath $p -TaskName $n -ErrorAction SilentlyContinue; " +
            $"if ($t) {{ Write-Output ($_ + '{ScheduledTaskQuery.Separator}' + $t.State) }} }}";

        ProcessRunResult result = await RunAsync(script, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            _logger.LogDebug("Scheduled task query failed: {Error}", result.Output.Trim());
            return [];
        }

        return ScheduledTaskQuery.Parse(result.StandardOutput);
    }

    public async Task<OperationResult> SetEnabledAsync(
        IReadOnlyList<string> paths,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
        {
            return OperationResult.NoChange();
        }

        if (!_environment.IsElevated)
        {
            return OperationResult.Fail(CoreMessages.ScheduledTaskNeedsAdministrator);
        }

        string verb = enabled ? "Enable-ScheduledTask" : "Disable-ScheduledTask";

        // Errors are collected and printed rather than thrown, so one task missing on this edition
        // of Windows does not stop the other five from being changed.
        string script =
            List(paths) + " | ForEach-Object { " + SplitPath +
            "try { " +
            $"{verb} -TaskPath $p -TaskName $n -ErrorAction Stop | Out-Null }} " +
            "catch { Write-Output ('FAILED ' + $_.Exception.Message) } }";

        ProcessRunResult result = await RunAsync(script, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return OperationResult.Fail(CoreMessages.ScheduledTaskChangeFailed, result.Output.Trim());
        }

        string failures = string.Join(
            "; ",
            result.StandardOutput
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("FAILED", StringComparison.Ordinal)));

        if (failures.Length > 0)
        {
            _logger.LogWarning("Some scheduled tasks could not be changed: {Failures}", failures);
            return OperationResult.Fail(CoreMessages.ScheduledTaskChangeFailed, failures);
        }

        _logger.LogInformation("{Count} scheduled tasks {Action}", paths.Count, enabled ? "enabled" : "disabled");
        return OperationResult.Ok();
    }

    private static string List(IReadOnlyList<string> paths) =>
        "@(" + string.Join(',', paths.Select(Quote)) + ")";

    /// <summary>
    /// Runs the script through the base64 path rather than building a command line.
    ///
    /// The first version of this quoted the script onto <c>powershell -Command "..."</c> by hand,
    /// which means surviving both the Windows command line parser and PowerShell's own. Every
    /// scheme anyone writes for that is a bug waiting for a path with a quote in it - and there was
    /// already a method here that encodes the command instead, so there is nothing to get right.
    /// </summary>
    private Task<ProcessRunResult> RunAsync(string script, CancellationToken cancellationToken) =>
        _processes.RunPowerShellAsync(script, Timeout, cancellationToken);

    /// <summary>
    /// A single-quoted PowerShell string, with any single quote in the path doubled. The paths are
    /// ours rather than the user's, but building a script by concatenation is how a quoting bug
    /// becomes a command nobody asked for.
    /// </summary>
    private static string Quote(string path) => "'" + path.Replace("'", "''", StringComparison.Ordinal) + "'";
}
