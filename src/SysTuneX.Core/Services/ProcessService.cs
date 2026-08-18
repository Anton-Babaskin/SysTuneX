using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Native;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IProcessService"/>
[SupportedOSPlatform("windows")]
public sealed class ProcessService : IProcessService
{
    private readonly ILogger<ProcessService> _logger;
    private readonly IEnvironmentService _environment;

    public ProcessService(ILogger<ProcessService> logger, IEnvironmentService environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public OperationResult SetPriority(int processId, ProcessPriorityClass priority)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.PriorityClass == priority)
            {
                return OperationResult.NoChange();
            }

            process.PriorityClass = priority;
            _logger.LogInformation("Priority of PID {Pid} set to {Priority}", processId, priority);
            return OperationResult.Ok();
        }
        catch (ArgumentException)
        {
            return OperationResult.Fail($"Process {processId} is no longer running.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Could not set priority for PID {processId}: {ex.Message}", ex);
        }
    }

    public OperationResult SetAffinity(int processId, nint affinityMask)
    {
        if (affinityMask == 0)
        {
            return OperationResult.Fail("An affinity mask of zero would leave the process no cores to run on.");
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            process.ProcessorAffinity = affinityMask;
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Could not set affinity for PID {processId}: {ex.Message}", ex);
        }
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

    public IReadOnlyList<ProcessInfo> GetTopProcessesByMemory(int count = 10)
    {
        var results = new List<ProcessInfo>();

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                results.Add(new ProcessInfo(process.Id, process.ProcessName, process.WorkingSet64, null));
            }
            catch
            {
                // The process exited between enumeration and inspection.
            }
            finally
            {
                process.Dispose();
            }
        }

        return results
            .OrderByDescending(p => p.WorkingSetBytes)
            .Take(count)
            .ToList();
    }

    private static long GetAvailableBytes() =>
        NativeHelpers.TryGetMemoryStatus(out NativeMethods.MEMORYSTATUSEX status) ? (long)status.AvailPhys : 0;
}
