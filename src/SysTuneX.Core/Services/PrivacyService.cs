using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IPrivacyService"/>
[SupportedOSPlatform("windows")]
public sealed class PrivacyService : IPrivacyService
{
    private const string BeginMarker = "# >>> SysTuneX telemetry block - do not edit inside this section";
    private const string EndMarker = "# <<< SysTuneX telemetry block";
    private const string OwnerId = "privacy:hosts";

    private readonly ILogger<PrivacyService> _logger;
    private readonly IBackupService _backup;
    private readonly IEnvironmentService _environment;

    public PrivacyService(ILogger<PrivacyService> logger, IBackupService backup, IEnvironmentService environment)
    {
        _logger = logger;
        _backup = backup;
        _environment = environment;
    }

    private static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers",
        "etc",
        "hosts");

    public IReadOnlyList<string> GetTelemetryHosts() => TelemetryHosts.All;

    public bool AreTelemetryHostsBlocked()
    {
        try
        {
            return File.Exists(HostsPath) && File.ReadAllText(HostsPath).Contains(BeginMarker, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the hosts file");
            return false;
        }
    }

    public async Task<OperationResult> BlockTelemetryHostsAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsElevated)
        {
            return OperationResult.Fail(CoreMessages.HostsNeedsAdministrator);
        }

        try
        {
            if (AreTelemetryHostsBlocked())
            {
                return OperationResult.NoChange();
            }

            List<string> lines = File.Exists(HostsPath)
                ? [.. await File.ReadAllLinesAsync(HostsPath, cancellationToken).ConfigureAwait(false)]
                : [];

            await BackupOriginalHostsAsync(cancellationToken).ConfigureAwait(false);

            var builder = new StringBuilder();
            foreach (string line in lines)
            {
                builder.AppendLine(line);
            }

            builder.AppendLine();
            builder.AppendLine(BeginMarker);
            builder.AppendLine($"# Added {DateTime.Now:yyyy-MM-dd HH:mm}. Remove this section to undo.");

            foreach (string host in TelemetryHosts.All)
            {
                builder.AppendLine($"0.0.0.0 {host}");
            }

            builder.AppendLine(EndMarker);

            await WriteHostsAsync(builder.ToString(), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Blocked {Count} telemetry hosts", TelemetryHosts.All.Count);
            return OperationResult.Ok();
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail(CoreMessages.HostsLocked, ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not block telemetry hosts");
            return OperationResult.Fail(CoreMessages.HostsUpdateFailed, ex, ex.Message);
        }
    }

    public async Task<OperationResult> UnblockTelemetryHostsAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsElevated)
        {
            return OperationResult.Fail(CoreMessages.HostsNeedsAdministrator);
        }

        try
        {
            if (!File.Exists(HostsPath))
            {
                return OperationResult.NoChange();
            }

            string[] lines = await File.ReadAllLinesAsync(HostsPath, cancellationToken).ConfigureAwait(false);

            int start = Array.FindIndex(lines, l => l.Contains(BeginMarker, StringComparison.Ordinal));
            if (start < 0)
            {
                return OperationResult.NoChange();
            }

            int end = Array.FindIndex(lines, start, l => l.Contains(EndMarker, StringComparison.Ordinal));

            // A truncated section (no end marker) is cut to the end of the file rather than left behind.
            var kept = new List<string>(lines.Length);
            kept.AddRange(lines[..start]);
            if (end >= 0 && end + 1 < lines.Length)
            {
                kept.AddRange(lines[(end + 1)..]);
            }

            // Drop the blank line the block was preceded by, so repeated toggling does not grow the file.
            while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
            {
                kept.RemoveAt(kept.Count - 1);
            }

            await WriteHostsAsync(string.Join(Environment.NewLine, kept) + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);

            await _backup.MarkRevertedAsync(BackupKind.HostsFile, HostsPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Removed the SysTuneX hosts block");
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not unblock telemetry hosts");
            return OperationResult.Fail(CoreMessages.HostsUpdateFailed, ex, ex.Message);
        }
    }

    /// <summary>Keeps a verbatim copy of the pre-change hosts file next to the journal.</summary>
    private async Task BackupOriginalHostsAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_environment.DataDirectory);
            string copyPath = Path.Combine(_environment.DataDirectory, "hosts.original");

            if (!File.Exists(copyPath) && File.Exists(HostsPath))
            {
                File.Copy(HostsPath, copyPath, overwrite: false);
            }

            await _backup.RecordRawAsync(
                    new BackupEntry
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Kind = BackupKind.HostsFile,
                        OwnerId = OwnerId,
                        Target = HostsPath,
                        OriginalValue = copyPath,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not archive the original hosts file");
        }
    }

    /// <summary>
    /// Writes through a temporary file and a move, so a failure part-way cannot leave Windows
    /// with a half-written hosts file and no name resolution.
    /// </summary>
    private static async Task WriteHostsAsync(string content, CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(Path.GetDirectoryName(HostsPath)!, $"hosts.systunex.{Environment.ProcessId}");

        // The hosts file has no BOM and Windows expects ANSI/UTF-8 without one.
        await File.WriteAllTextAsync(tempPath, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, HostsPath, overwrite: true);
    }
}
