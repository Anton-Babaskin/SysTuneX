using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>Facts about the process and the OS that gate what SysTuneX is allowed to do.</summary>
public interface IEnvironmentService
{
    /// <summary>The process holds a full administrator token.</summary>
    bool IsElevated { get; }

    /// <summary>Real OS version, read through RtlGetVersion so compatibility shims cannot lie about it.</summary>
    WindowsVersionInfo Windows { get; }

    /// <summary>Relaunches the app through the UAC prompt and asks the current instance to exit.</summary>
    OperationResult RestartElevated();

    /// <summary>Restarts explorer.exe so shell-level tweaks become visible without a reboot.</summary>
    Task<OperationResult> RestartExplorerAsync(CancellationToken cancellationToken = default);

    /// <summary>Directory SysTuneX keeps its backup journal and log in.</summary>
    string DataDirectory { get; }
}
