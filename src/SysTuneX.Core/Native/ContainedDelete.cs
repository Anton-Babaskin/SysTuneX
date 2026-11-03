using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using static SysTuneX.Core.Native.NativeMethods;

namespace SysTuneX.Core.Native;

/// <summary>
/// Deletes a file or folder only if it really is inside the folder being cleaned.
///
/// "Really" is the whole point. %SystemRoot%\Temp lets every user create folders in it, and a
/// folder there can be swapped for a junction between the moment the cleaner lists it and the
/// moment it deletes what was in it - after which C:\Windows\Temp\x\setup.dll is a name for a
/// file in System32, and the cleaner runs as administrator. Checking the path first cannot help,
/// because the swap happens after the check. So the file is opened first, without following a link
/// at the end of the path; the open file is asked where it actually is; and the delete goes through
/// that same handle, which keeps meaning the file that was checked whatever happens to the names
/// around it afterwards.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ContainedDelete
{
    internal enum Outcome
    {
        Deleted,

        /// <summary>In use, protected, already gone, or a folder that still has something in it.</summary>
        Failed,

        /// <summary>The name led somewhere outside the root. Nothing was deleted.</summary>
        OutsideRoot,
    }

    /// <summary>
    /// Where a folder to be cleaned really is, or null when it cannot be trusted as one.
    ///
    /// Null when the folder, or any folder above it, is a junction or link. Some cleanup roots sit
    /// where a standard user can create them before the program that owns them does -
    /// %ProgramData%\NVIDIA Corporation on a machine without an NVIDIA driver is one - and a root
    /// that is itself a link to System32 would make everything in System32 look contained.
    /// </summary>
    internal static string? ResolveRoot(string root)
    {
        try
        {
            for (DirectoryInfo? directory = new(root); directory is not null; directory = directory.Parent)
            {
                // A missing folder reads as every attribute at once, this one included; one that
                // cannot be read at all ends up in the catch. Either way it is not a root.
                if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return null;
                }
            }

            using SafeFileHandle handle = Open(root, FILE_READ_ATTRIBUTES, directory: true);
            if (handle.IsInvalid || FinalPath(handle) is not { } resolved)
            {
                return null;
            }

            return resolved.TrimEnd('\\') + '\\';
        }
        catch
        {
            return null;
        }
    }

    /// <param name="resolvedRoot">From <see cref="ResolveRoot"/>, taken once before the walk began.</param>
    /// <param name="directory">A folder is only deleted when empty; Windows refuses otherwise.</param>
    internal static Outcome Delete(string path, string resolvedRoot, bool directory)
    {
        using SafeFileHandle handle = Open(path, DELETE | FILE_READ_ATTRIBUTES, directory);
        if (handle.IsInvalid)
        {
            return Outcome.Failed;
        }

        if (FinalPath(handle) is not { } actual ||
            !actual.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Outcome.OutsideRoot;
        }

        // Shader caches are full of read-only leftovers. Ignoring the flag here rather than
        // clearing it first means no write ever goes through the path, only through this handle.
        var extended = new FILE_DISPOSITION_INFO_EX
        {
            Flags = FILE_DISPOSITION_FLAG_DELETE | FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE,
        };

        if (SetFileInformationByHandle(handle, FileDispositionInfoEx, ref extended, Marshal.SizeOf<FILE_DISPOSITION_INFO_EX>()))
        {
            return Outcome.Deleted;
        }

        // FAT volumes and some network shares only know the plain form, which leaves read-only
        // files where they are.
        var plain = new FILE_DISPOSITION_INFO { DeleteFile = 1 };
        return SetFileInformationByHandle(handle, FileDispositionInfo, ref plain, Marshal.SizeOf<FILE_DISPOSITION_INFO>())
            ? Outcome.Deleted
            : Outcome.Failed;
    }

    /// <summary>Opens the name itself: a link at the end of the path is opened, not followed.</summary>
    private static SafeFileHandle Open(string path, uint access, bool directory) =>
        CreateFile(
            path,
            access,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OPEN_REPARSE_POINT | (directory ? FILE_FLAG_BACKUP_SEMANTICS : 0),
            IntPtr.Zero);

    /// <summary>Where the open file actually is, every junction on the way already resolved.</summary>
    private static string? FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        while (true)
        {
            // On success the length excludes the terminator; when the buffer is too small it is
            // the size needed including it, which is never less than the buffer just passed.
            uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, VOLUME_NAME_NT);
            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Length)
            {
                return new string(buffer, 0, (int)length);
            }

            buffer = new char[length];
        }
    }
}
