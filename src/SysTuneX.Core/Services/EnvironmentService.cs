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

    private readonly ILogger<EnvironmentService> _logger;
    private readonly IRegistryService _registry;
    private readonly Lazy<bool> _isElevated;
    private readonly Lazy<WindowsVersionInfo> _windows;

    public EnvironmentService(ILogger<EnvironmentService> logger, IRegistryService registry)
    {
        _logger = logger;
        _registry = registry;
        _isElevated = new Lazy<bool>(DetectElevation);
        _windows = new Lazy<WindowsVersionInfo>(DetectWindowsVersion);

        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SysTuneX");
    }

    public bool IsElevated => _isElevated.Value;

    public WindowsVersionInfo Windows => _windows.Value;

    public string DataDirectory { get; }

    public OperationResult RestartElevated()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return OperationResult.Fail("Could not resolve the SysTuneX executable path.");
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
            return OperationResult.Fail("The elevation prompt was cancelled or blocked by policy.", ex);
        }
    }

    public async Task<OperationResult> RestartExplorerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Process[] explorers = Process.GetProcessesByName("explorer");
            foreach (Process explorer in explorers)
            {
                try
                {
                    explorer.Kill();
                    await explorer.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not stop explorer PID {Pid}", explorer.Id);
                }
                finally
                {
                    explorer.Dispose();
                }
            }

            // Windows normally relaunches the shell on its own; start it if it did not.
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }

            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Could not restart Explorer: {ex.Message}", ex);
        }
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
