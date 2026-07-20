using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Services;

public partial class ProcessService : IProcessService
{
    private readonly ILogger<ProcessService> _logger;

    // P/Invoke for memory cleanup
    [LibraryImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyWorkingSet(nint hProcess);

    public ProcessService(ILogger<ProcessService> logger)
    {
        _logger = logger;
    }

    public bool SetProcessPriority(int pid, ProcessPriorityClass priority)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            process.PriorityClass = priority;
            _logger.LogInformation("Set priority {Priority} for PID {Pid}", priority, pid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set priority for PID {Pid}", pid);
            return false;
        }
    }

    public bool SetProcessAffinity(int pid, nint affinityMask)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            process.ProcessorAffinity = affinityMask;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set affinity for PID {Pid}", pid);
            return false;
        }
    }

    public int CleanStandbyMemory()
    {
        int cleaned = 0;
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    EmptyWorkingSet(process.Handle);
                    cleaned++;
                }
                catch
                {
                    // Skip processes we can't access
                }
            }
            _logger.LogInformation("Cleaned working set for {Count} processes", cleaned);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean standby memory");
        }
        return cleaned;
    }

    public List<(int Pid, string Name, long MemoryMb)> GetTopProcesses(int count = 10)
    {
        try
        {
            return Process.GetProcesses()
                .OrderByDescending(p => p.WorkingSet64)
                .Take(count)
                .Select(p => (p.Id, p.ProcessName, p.WorkingSet64 / (1024 * 1024)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get top processes");
            return [];
        }
    }
}
