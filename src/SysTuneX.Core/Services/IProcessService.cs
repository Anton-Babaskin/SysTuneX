namespace SysTuneX.Core.Services;

public interface IProcessService
{
    bool SetProcessPriority(int pid, System.Diagnostics.ProcessPriorityClass priority);
    bool SetProcessAffinity(int pid, nint affinityMask);
    int CleanStandbyMemory();
    List<(int Pid, string Name, long MemoryMb)> GetTopProcesses(int count = 10);
}
