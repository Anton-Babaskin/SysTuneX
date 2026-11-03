using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

// Every DllImport in this assembly loads from System32 and nowhere else. Without this the folder
// SysTuneX.exe was started from is searched first, and for nvml.dll and atiadlxx.dll - not on the
// list of DLLs Windows always takes from System32 - a copy left in Downloads would be loaded into
// an administrator process. On a machine without that vendor's driver it is the only copy there is.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace SysTuneX.Core.Native;

/// <summary>
/// Win32 entry points SysTuneX needs. Everything here is a documented API — the project
/// deliberately avoids undocumented behaviour except for the two well-known ntdll memory
/// calls, which are guarded and fall back to a supported path when they fail.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class NativeMethods
{
    // ── kernel32 ────────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetCurrentProcess();

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint PROCESS_SET_QUOTA = 0x0100;

    // ── kernel32: files ─────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW")]
    internal static extern uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] path, uint length, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref FILE_DISPOSITION_INFO_EX information, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref FILE_DISPOSITION_INFO information, int size);

    internal const uint DELETE = 0x00010000;
    internal const uint FILE_READ_ATTRIBUTES = 0x0080;
    internal const uint FILE_SHARE_READ = 0x1;
    internal const uint FILE_SHARE_WRITE = 0x2;
    internal const uint FILE_SHARE_DELETE = 0x4;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    internal const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    /// <summary>\Device\HarddiskVolume3\... - needs no drive letter, so it works on every volume.</summary>
    internal const uint VOLUME_NAME_NT = 0x2;

    internal const int FileDispositionInfo = 4;
    internal const int FileDispositionInfoEx = 21;
    internal const uint FILE_DISPOSITION_FLAG_DELETE = 0x01;
    internal const uint FILE_DISPOSITION_FLAG_IGNORE_READONLY_ATTRIBUTE = 0x10;

    // ── psapi ───────────────────────────────────────────────────────────────────

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(IntPtr processHandle);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION performanceInformation, int size);

    // ── ntdll ───────────────────────────────────────────────────────────────────

    /// <summary>Reports the real OS version, unaffected by application compatibility shims.</summary>
    [DllImport("ntdll.dll")]
    internal static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEXW versionInformation);

    [DllImport("ntdll.dll")]
    internal static extern int NtQuerySystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength, out int returnLength);

    [DllImport("ntdll.dll")]
    internal static extern int NtSetSystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength);

    internal const int SystemMemoryListInformation = 0x50;

    /// <summary>Command accepted by <c>NtSetSystemInformation(SystemMemoryListInformation)</c> to drop the standby list.</summary>
    internal const int MemoryPurgeStandbyList = 4;

    // ── user32 ──────────────────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint action, uint param, IntPtr pointerValue, uint winIni);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfoArray(uint action, uint param, int[] pointerValue, uint winIni);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    internal static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    internal const uint WM_SETTINGCHANGE = 0x001A;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;

    internal const uint SPI_SETMOUSE = 0x0004;
    internal const uint SPI_SETMOUSESPEED = 0x0071;
    internal const uint SPI_SETUIEFFECTS = 0x103F;
    internal const uint SPIF_UPDATEINIFILE = 0x01;
    internal const uint SPIF_SENDCHANGE = 0x02;

    // ── advapi32: service control ───────────────────────────────────────────────

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenSCManagerW")]
    internal static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenServiceW")]
    internal static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "ChangeServiceConfigW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeServiceConfig(
        IntPtr service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseServiceHandle(IntPtr handle);

    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SERVICE_QUERY_CONFIG = 0x0001;
    internal const uint SERVICE_CHANGE_CONFIG = 0x0002;
    internal const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;

    // ── advapi32: token privileges ──────────────────────────────────────────────

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeValue(string? systemName, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        int bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const uint SE_PRIVILEGE_ENABLED = 0x0002;

    // ── structures ──────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;

        public static MEMORYSTATUSEX Create() => new() { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PERFORMANCE_INFORMATION
    {
        public int Size;
        public IntPtr CommitTotal;
        public IntPtr CommitLimit;
        public IntPtr CommitPeak;
        public IntPtr PhysicalTotal;
        public IntPtr PhysicalAvailable;
        public IntPtr SystemCache;
        public IntPtr KernelTotal;
        public IntPtr KernelPaged;
        public IntPtr KernelNonpaged;
        public IntPtr PageSize;
        public int HandleCount;
        public int ProcessCount;
        public int ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RTL_OSVERSIONINFOEXW
    {
        public uint OSVersionInfoSize;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint BuildNumber;
        public uint PlatformId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string CSDVersion;

        public ushort ServicePackMajor;
        public ushort ServicePackMinor;
        public ushort SuiteMask;
        public byte ProductType;
        public byte Reserved;

        public static RTL_OSVERSIONINFOEXW Create() =>
            new() { OSVersionInfoSize = (uint)Marshal.SizeOf<RTL_OSVERSIONINFOEXW>(), CSDVersion = string.Empty };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_DISPOSITION_INFO_EX
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILE_DISPOSITION_INFO
    {
        /// <summary>A Win32 BOOLEAN, one byte wide.</summary>
        public byte DeleteFile;
    }
}
