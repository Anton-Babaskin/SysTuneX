using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Native;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IMemoryTrimmer"/>
[SupportedOSPlatform("windows")]
public sealed class ProcessService : IMemoryTrimmer
{
    private readonly ILogger<ProcessService> _logger;
    private readonly IEnvironmentService _environment;

    public ProcessService(ILogger<ProcessService> logger, IEnvironmentService environment)
    {
        _logger = logger;
        _environment = environment;
    }



    /// <summary>
    /// Trims every accessible working set and then drops the standby list.
    ///
    /// The old implementation touched <c>Process.Handle</c>, which asks for full access and
    /// throws on most system processes; this opens the minimum rights EmptyWorkingSet needs
    /// and always closes the handle.
    /// </summary>
    public Task<MemoryTrimResult> TrimMemoryAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                long availableBefore = GetAvailableBytes();
                int trimmed = 0;

                foreach (Process process in Process.GetProcesses())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Trimming our own working set just makes the app stutter for nothing.
                        if (process.Id != Environment.ProcessId && NativeHelpers.TrimProcessWorkingSet(process.Id))
                        {
                            trimmed++;
                        }
                    }
                    catch
                    {
                        // Protected processes are expected to refuse; that is not a failure.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                bool standbyPurged = _environment.IsElevated && NativeHelpers.PurgeStandbyList();
                long availableAfter = GetAvailableBytes();

                _logger.LogInformation(
                    "Trimmed {Count} working sets, standby purge {Purged}",
                    trimmed,
                    standbyPurged ? "succeeded" : "skipped");

                return new MemoryTrimResult(trimmed, standbyPurged, Math.Max(0, availableAfter - availableBefore));
            },
            cancellationToken);
    }


    private static long GetAvailableBytes() =>
        NativeHelpers.TryGetMemoryStatus(out NativeMethods.MEMORYSTATUSEX status) ? (long)status.AvailPhys : 0;
}
