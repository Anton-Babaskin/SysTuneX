using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Native;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IDesktopSession"/>
[SupportedOSPlatform("windows")]
public sealed class DesktopSession : IDesktopSession
{
    private readonly ILogger<DesktopSession> _logger;
    private readonly Lazy<SeparateElevation?> _separate;

    public DesktopSession(ILogger<DesktopSession> logger)
    {
        _logger = logger;
        _separate = new Lazy<SeparateElevation?>(Detect);
    }

    public SeparateElevation? SeparateElevation => _separate.Value;

    /// <summary>
    /// Compares SIDs, not names: a name can be spelled more than one way, a SID cannot. Anything
    /// that cannot be read or translated leaves the answer at "the same account", which is what
    /// SysTuneX assumed before it asked at all.
    /// </summary>
    private SeparateElevation? Detect()
    {
        try
        {
            using WindowsIdentity process = WindowsIdentity.GetCurrent();
            if (process.User is not { } processSid)
            {
                return null;
            }

            string? desktop = NativeHelpers.GetSessionAccountName();
            if (string.IsNullOrEmpty(desktop))
            {
                return null;
            }

            var desktopSid = (SecurityIdentifier)new NTAccount(desktop).Translate(typeof(SecurityIdentifier));
            if (desktopSid == processSid)
            {
                return null;
            }

            // Only point at a hive that is there. A signed-in account's always is; if this one is
            // not, writing where SysTuneX always wrote is the smaller surprise.
            using (RegistryKey users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64))
            using (RegistryKey? hive = users.OpenSubKey(desktopSid.Value))
            {
                if (hive is null)
                {
                    _logger.LogWarning(
                        "SysTuneX runs as {Elevated}, not as the desktop's {Desktop}, whose hive is not loaded; per-user settings stay with the elevated account",
                        process.Name,
                        desktop);
                    return null;
                }
            }

            var separate = new SeparateElevation(process.Name, desktop, desktopSid.Value);
            _logger.LogInformation(
                "SysTuneX runs as {Elevated} and the desktop belongs to {Desktop}; per-user settings are written to the desktop account's hive",
                separate.ElevatedAccount,
                separate.DesktopAccount);
            return separate;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not compare the process account with the desktop's");
            return null;
        }
    }
}
