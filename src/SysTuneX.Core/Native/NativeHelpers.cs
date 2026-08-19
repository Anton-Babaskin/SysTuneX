using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static SysTuneX.Core.Native.NativeMethods;

namespace SysTuneX.Core.Native;

/// <summary>Thin, exception-safe wrappers around <see cref="NativeMethods"/>.</summary>
[SupportedOSPlatform("windows")]
internal static class NativeHelpers
{
    /// <summary>Enables a privilege on the current process token. Returns false when the token does not hold it.</summary>
    internal static bool EnablePrivilege(string privilegeName)
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                return false;

            if (!LookupPrivilegeValue(null, privilegeName, out LUID luid))
                return false;

            var privileges = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED,
            };

            if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero))
                return false;

            // AdjustTokenPrivileges reports success even when it only assigned some privileges.
            return Marshal.GetLastWin32Error() == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero)
            {
                CloseHandle(token);
            }
        }
    }

    /// <summary>Tells every top-level window that a system setting changed, so running apps pick it up.</summary>
    internal static void BroadcastSettingChange(string section = "")
    {
        try
        {
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, section, SMTO_ABORTIFHUNG, 1000, out _);
        }
        catch
        {
            // Purely a live-refresh nicety; the registry write already happened.
        }
    }

    /// <summary>
    /// Pushes the mouse acceleration triple into the running session. Writing
    /// <c>HKCU\Control Panel\Mouse</c> alone does nothing until this call or a sign-out.
    /// </summary>
    internal static bool ApplyMouseSettings(bool accelerationEnabled)
    {
        try
        {
            // { threshold1, threshold2, acceleration } — all zero means raw 1:1 input.
            int[] settings = accelerationEnabled ? [6, 10, 1] : [0, 0, 0];
            return SystemParametersInfoArray(SPI_SETMOUSE, 0, settings, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Turns the desktop UI animations on or off for the current session.</summary>
    internal static bool ApplyUiEffects(bool enabled)
    {
        try
        {
            return SystemParametersInfo(SPI_SETUIEFFECTS, 0, enabled ? new IntPtr(1) : IntPtr.Zero, SPIF_SENDCHANGE);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryGetMemoryStatus(out MEMORYSTATUSEX status)
    {
        status = MEMORYSTATUSEX.Create();
        try
        {
            return GlobalMemoryStatusEx(ref status);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryGetPerformanceInfo(out PERFORMANCE_INFORMATION info)
    {
        info = default;
        try
        {
            return GetPerformanceInfo(out info, Marshal.SizeOf<PERFORMANCE_INFORMATION>());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Standby (cached) pages in bytes. Uses SystemMemoryListInformation when available and
    /// falls back to the documented GetPerformanceInfo system-cache figure.
    /// </summary>
    internal static long GetStandbyBytes()
    {
        long pageSize = TryGetPerformanceInfo(out var perf) ? perf.PageSize.ToInt64() : 4096;

        // SYSTEM_MEMORY_LIST_INFORMATION: 5 leading counters, then 8 standby priority buckets.
        const int leadingCounters = 5;
        const int priorityBuckets = 8;
        int pointerSize = IntPtr.Size;
        int bufferSize = pointerSize * (leadingCounters + priorityBuckets * 2 + 1);

        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (NtQuerySystemInformation(SystemMemoryListInformation, buffer, bufferSize, out _) == 0)
            {
                long standbyPages = 0;
                for (int i = 0; i < priorityBuckets; i++)
                {
                    IntPtr slot = IntPtr.Add(buffer, pointerSize * (leadingCounters + i));
                    standbyPages += pointerSize == 8 ? Marshal.ReadInt64(slot) : Marshal.ReadInt32(slot);
                }

                if (standbyPages > 0)
                {
                    return standbyPages * pageSize;
                }
            }
        }
        catch
        {
            // Fall through to the supported approximation.
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return perf.SystemCache.ToInt64() * pageSize;
    }

    /// <summary>
    /// Drops the standby list. Needs SeProfileSingleProcessPrivilege, which only an
    /// elevated token has — returns false rather than pretending it worked.
    /// </summary>
    internal static bool PurgeStandbyList()
    {
        if (!EnablePrivilege("SeProfileSingleProcessPrivilege"))
            return false;

        IntPtr command = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(command, MemoryPurgeStandbyList);
            return NtSetSystemInformation(SystemMemoryListInformation, command, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(command);
        }
    }

    /// <summary>Trims one process's working set without taking a full-rights handle on it.</summary>
    internal static bool TrimProcessWorkingSet(int processId)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA, false, processId);
            return handle != IntPtr.Zero && EmptyWorkingSet(handle);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }
    }
}
