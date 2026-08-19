using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

public interface IPrivacyService
{
    /// <summary>SysTuneX's block section is present in the hosts file.</summary>
    bool AreTelemetryHostsBlocked();

    /// <summary>
    /// Appends a clearly delimited block to the hosts file, backing up the original file first.
    /// Only SysTuneX's own section is ever touched.
    /// </summary>
    Task<OperationResult> BlockTelemetryHostsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> UnblockTelemetryHostsAsync(CancellationToken cancellationToken = default);

    /// <summary>Hosts the block section would add, so the UI can show them before writing anything.</summary>
    IReadOnlyList<string> GetTelemetryHosts();
}
