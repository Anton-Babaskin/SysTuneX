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

    /// <summary>
    /// Reboots the machine, after a short delay so the user can still cancel from a console.
    ///
    /// Here rather than in the view model that offers the button. Rebooting somebody's PC is the
    /// single most consequential thing this application does, and it had been a one-line call to a
    /// static process runner sitting in a view model - unreachable from a test, and in a layer
    /// whose job is to describe a screen.
    /// </summary>
    Task<OperationResult> RestartWindowsAsync(CancellationToken cancellationToken = default);

    /// <summary>Directory SysTuneX keeps its backup journal and log in.</summary>
    string DataDirectory { get; }
}
