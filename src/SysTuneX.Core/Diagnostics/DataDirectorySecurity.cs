using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SysTuneX.Core.Diagnostics;

/// <summary>
/// Makes %ProgramData%\SysTuneX writable by administrators only, before anything reads from it.
///
/// A new folder under ProgramData inherits rules that let every user on the machine create files
/// in it, and whoever creates a file owns it. This folder is SysTuneX's memory: the journal says
/// which registry values to write back and which scheduled tasks to switch on again, the game-mode
/// session says which services to start. With the inherited rules any standard user could write
/// that memory - or create the whole folder before SysTuneX first ran - and the next administrator
/// to click "Restore everything" would carry it out.
///
/// Static for the same reason <see cref="AppPaths"/> is: it has to run before the logger, and so
/// before the container, puts anything in there.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DataDirectorySecurity
{
    /// <summary>
    /// Locks the folder down, creating it if it is not there.
    ///
    /// Anything inside that an administrator did not create, or a junction anywhere in it, means
    /// none of it can be trusted - so the whole folder is set aside under a new name and an empty
    /// one made in its place. Starting with no journal loses the record of past changes; starting
    /// with a forged one would replay whatever the forger wrote.
    /// </summary>
    /// <param name="directory">Defaults to %ProgramData%\SysTuneX; overridden by tests.</param>
    /// <returns>Where an untrusted folder was moved to, or null when there was none.</returns>
    /// <exception cref="Exception">The folder could not be made safe, and must not be used.</exception>
    public static string? Secure(string? directory = null)
    {
        string path = Path.GetFullPath(directory ?? AppPaths.DataDirectory);
        HashSet<SecurityIdentifier> owners = TrustedOwners();
        string? setAside = null;

        if (Path.Exists(path))
        {
            var existing = new DirectoryInfo(path);
            if (IsTrustedFolder(existing, owners))
            {
                // Locked first, looked through second: once this returns nothing new can appear,
                // so what the check below sees is everything there will be.
                existing.SetAccessControl(LockedDown());
                if (ContentsAreTrusted(existing, owners))
                {
                    return null;
                }
            }

            // Moves a junction as the link itself, and a file squatting on the name as a file.
            setAside = $"{path}.untrusted-{DateTime.Now:yyyyMMddHHmmss}";
            Directory.Move(path, setAside);
        }

        // Born locked down, so there is no moment at which it is open.
        new DirectoryInfo(path).Create(LockedDown());

        // Create does nothing when the folder already exists, which it would if someone made it
        // again in the instant after the move. Trust what is there, not what was asked for.
        var created = new DirectoryInfo(path);
        if (!IsTrustedFolder(created, owners) || !ContentsAreTrusted(created, owners))
        {
            throw new UnauthorizedAccessException(
                $"{path} is still not an administrator's folder after being created afresh. Something may have replaced it.");
        }

        return setAside;
    }

    /// <summary>
    /// The system, the Administrators group, and this account when it is elevated. Which of the
    /// last two owns what an administrator creates depends on a local policy setting, so both
    /// count. Not elevated, the account counts for nothing - and locking the folder fails anyway.
    /// </summary>
    private static HashSet<SecurityIdentifier> TrustedOwners()
    {
        HashSet<SecurityIdentifier> owners =
        [
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
        ];

        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        if (identity.User is { } user && new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            owners.Add(user);
        }

        return owners;
    }

    private static bool IsTrustedFolder(DirectoryInfo folder, HashSet<SecurityIdentifier> owners) =>
        folder.Attributes.HasFlag(FileAttributes.Directory) && IsTrusted(folder, owners);

    private static bool IsTrusted(FileSystemInfo entry, HashSet<SecurityIdentifier> owners)
    {
        try
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            FileSystemSecurity security = entry is DirectoryInfo folder
                ? folder.GetAccessControl(AccessControlSections.Owner)
                : ((FileInfo)entry).GetAccessControl(AccessControlSections.Owner);

            return security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && owners.Contains(owner);
        }
        catch
        {
            // Unreadable to an administrator means someone took the trouble to make it so.
            return false;
        }
    }

    /// <summary>Everything below, never through a junction - those fail <see cref="IsTrusted"/> first.</summary>
    private static bool ContentsAreTrusted(DirectoryInfo folder, HashSet<SecurityIdentifier> owners)
    {
        try
        {
            foreach (FileSystemInfo entry in folder.EnumerateFileSystemInfos())
            {
                if (!IsTrusted(entry, owners) ||
                    (entry is DirectoryInfo child && !ContentsAreTrusted(child, owners)))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static DirectorySecurity LockedDown()
    {
        var security = new DirectorySecurity();

        // Nothing inherited from ProgramData: its rules are the ones that let any user write here.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        security.AddAccessRule(Rule(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl));
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl));

        // Anyone can still read the log, which is what a bug report needs.
        security.AddAccessRule(Rule(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute));

        // An owner may always rewrite an object's permissions unless a rule says otherwise. This is
        // that rule: owning a file here gives read, and full control comes only from being an
        // administrator - not from being the account that happened to create it, unelevated.
        security.AddAccessRule(Rule(WellKnownSidType.WinCreatorOwnerRightsSid, FileSystemRights.ReadAndExecute));

        return security;

        static FileSystemAccessRule Rule(WellKnownSidType who, FileSystemRights rights) => new(
            new SecurityIdentifier(who, null),
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);
    }
}
