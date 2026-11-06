using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Native;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IEnvironmentService"/>
[SupportedOSPlatform("windows")]
public sealed class EnvironmentService : IEnvironmentService
{
    private const string CurrentVersionKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    /// <summary>How long Windows gets to bring an ended shell back before anything else is tried.</summary>
    private static readonly TimeSpan ShellReturnTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ShellPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly ILogger<EnvironmentService> _logger;
    private readonly IRegistryService _registry;
    private readonly IProcessRunner _processes;
    private readonly IDesktopSession _desktop;
    private readonly Lazy<bool> _isElevated;
    private readonly Lazy<WindowsVersionInfo> _windows;

    public EnvironmentService(
        ILogger<EnvironmentService> logger,
        IRegistryService registry,
        IProcessRunner processes,
        IDesktopSession desktop)
    {
        _logger = logger;
        _registry = registry;
        _processes = processes;
        _desktop = desktop;
        _isElevated = new Lazy<bool>(DetectElevation);
        _windows = new Lazy<WindowsVersionInfo>(DetectWindowsVersion);

        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SysTuneX");
    }

    public bool IsElevated => _isElevated.Value;

    public WindowsVersionInfo Windows => _windows.Value;

    public SeparateElevation? SeparateElevation => _desktop.SeparateElevation;

    public string DataDirectory { get; }

    public OperationResult RestartElevated()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return OperationResult.Fail(CoreMessages.EnvironmentExecutableUnknown);
            }

            // UseShellExecute + runas is what raises the UAC prompt.
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            };

            Process.Start(startInfo);
            return OperationResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Elevated restart was refused");
            return OperationResult.Fail(CoreMessages.EnvironmentElevationRefused, ex);
        }
    }

    /// <summary>
    /// Asks Windows to reboot in five seconds.
    ///
    /// The delay is deliberate: shutdown.exe can be called off with <c>shutdown /a</c> inside it,
    /// which is the only escape hatch there is once the command has gone out.
    /// </summary>
    public async Task<OperationResult> RestartWindowsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsElevated)
        {
            return OperationResult.Fail(CoreMessages.BootNeedsAdministrator);
        }

        ProcessRunResult result = await _processes
            .RunAsync("shutdown.exe", "/r /t 5", TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            _logger.LogWarning("shutdown.exe refused to restart the machine: {Error}", result.Output.Trim());
            return OperationResult.Fail(CoreMessages.RestartRefused, result.Output.Trim());
        }

        _logger.LogInformation("A restart was requested");
        return OperationResult.Ok();
    }

    /// <summary>
    /// Ends this desktop's shell and lets Windows bring it back.
    ///
    /// Three things this used to get wrong. It ended every explorer.exe on the machine - an
    /// administrator may - so a second user signed in through fast user switching lost their shell
    /// as well. It reported success whatever happened: when Windows refused to end the shell, as it
    /// does when the shell belongs to another account, the page said Explorer had restarted and
    /// nothing had. And it gave Windows two seconds to relaunch the shell before starting one itself
    /// - from an elevated process, so on a slow machine the taskbar came back running as
    /// administrator, and under Administrator protection as another account altogether.
    /// </summary>
    public async Task<OperationResult> RestartExplorerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            int session = CurrentSessionId();
            int stopped = 0;
            Exception? refusal = null;

            foreach (Process explorer in Process.GetProcessesByName("explorer"))
            {
                using (explorer)
                {
                    if (!IsInSession(explorer, session))
                    {
                        continue;
                    }

                    try
                    {
                        explorer.Kill();
                        await explorer.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                        stopped++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        refusal ??= ex;
                        _logger.LogDebug(ex, "Could not stop explorer PID {Pid}", explorer.Id);
                    }
                }
            }

            if (stopped == 0 && refusal is not null)
            {
                return SeparateElevation is { } separate
                    ? OperationResult.Fail(
                        CoreMessages.EnvironmentExplorerOtherAccount,
                        refusal,
                        separate.DesktopAccount,
                        separate.ElevatedAccount)
                    : OperationResult.Fail(CoreMessages.EnvironmentExplorerRestartFailed, refusal, refusal.Message);
            }

            // An ended shell is relaunched by Windows itself, as the account signed in to the
            // session - the only launch that is right in every case. Nothing to wait for when there
            // was no shell to end.
            if (stopped > 0 && await WaitForShellAsync(session, cancellationToken).ConfigureAwait(false))
            {
                return OperationResult.Ok();
            }

            if (SeparateElevation is { } elevated)
            {
                // Started from here it would run as the elevated account: somebody else's taskbar.
                return OperationResult.Fail(CoreMessages.EnvironmentExplorerNotBack, elevated.ElevatedAccount);
            }

            // By full path: a bare name is looked for in the working directory first.
            string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(shell) { UseShellExecute = true })?.Dispose();
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(CoreMessages.EnvironmentExplorerRestartFailed, ex, ex.Message);
        }
    }

    private static async Task<bool> WaitForShellAsync(int session, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + ShellReturnTimeout;

        do
        {
            await Task.Delay(ShellPollInterval, cancellationToken).ConfigureAwait(false);

            Process[] explorers = Process.GetProcessesByName("explorer");
            try
            {
                if (explorers.Any(explorer => IsInSession(explorer, session)))
                {
                    return true;
                }
            }
            finally
            {
                foreach (Process explorer in explorers)
                {
                    explorer.Dispose();
                }
            }
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    private static bool IsInSession(Process process, int session)
    {
        try
        {
            return process.SessionId == session;
        }
        catch (InvalidOperationException)
        {
            // Exited between the listing and the question.
            return false;
        }
    }

    private static int CurrentSessionId()
    {
        using Process current = Process.GetCurrentProcess();
        return current.SessionId;
    }

    private bool DetectElevation()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine elevation state");
            return false;
        }
    }

    private WindowsVersionInfo DetectWindowsVersion()
    {
        int major = Environment.OSVersion.Version.Major;
        int minor = Environment.OSVersion.Version.Minor;
        int build = Environment.OSVersion.Version.Build;

        // RtlGetVersion ignores compatibility shims, so it is the number to trust.
        try
        {
            var info = NativeMethods.RTL_OSVERSIONINFOEXW.Create();
            if (NativeMethods.RtlGetVersion(ref info) == 0 && info.BuildNumber > 0)
            {
                major = (int)info.MajorVersion;
                minor = (int)info.MinorVersion;
                build = (int)info.BuildNumber;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RtlGetVersion unavailable, falling back to Environment.OSVersion");
        }

        string productName = _registry.GetValue(CurrentVersionKey, "ProductName") as string ?? "Windows";
        string displayVersion = _registry.GetValue(CurrentVersionKey, "DisplayVersion") as string ?? string.Empty;
        string edition = _registry.GetValue(CurrentVersionKey, "EditionID") as string ?? string.Empty;
        int revision = _registry.GetValue(CurrentVersionKey, "UBR") is int ubr ? ubr : 0;

        // Windows 11 keeps reporting "Windows 10 ..." in ProductName; correct it from the build number.
        if (build >= 22000 && productName.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            productName = productName.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        return new WindowsVersionInfo
        {
            Major = major,
            Minor = minor,
            Build = build,
            Revision = revision,
            ProductName = productName,
            DisplayVersion = displayVersion,
            Edition = edition,
        };
    }
}
