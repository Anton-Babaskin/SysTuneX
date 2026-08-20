namespace SysTuneX.Core.Models;

/// <summary>
/// Every message the system layer can hand to the user, in one place.
///
/// Keeping them here rather than as literals at the throw site is what makes them translatable:
/// each carries a stable code the UI resolves against its own resources, and a test asserts that
/// every code has a translation in every shipped language with the same number of placeholders.
/// </summary>
public static class CoreMessages
{
    // ── Registry ────────────────────────────────────────────────────────────────

    public static readonly MessageTemplate RegistryOpenFailed =
        new("Registry_OpenFailed", "Could not open or create {0}.");

    public static readonly MessageTemplate RegistryValueNotKept =
        new("Registry_ValueNotKept", "{0}\\{1} did not keep the written value.");

    public static readonly MessageTemplate RegistryAccessDeniedWrite =
        new("Registry_AccessDeniedWrite", "Access denied writing {0}\\{1}. Run SysTuneX as administrator.");

    public static readonly MessageTemplate RegistryWriteFailed =
        new("Registry_WriteFailed", "Failed to write {0}\\{1}: {2}");

    public static readonly MessageTemplate RegistryAccessDeniedDelete =
        new("Registry_AccessDeniedDelete", "Access denied deleting {0}\\{1}.");

    public static readonly MessageTemplate RegistryDeleteFailed =
        new("Registry_DeleteFailed", "Failed to delete {0}\\{1}: {2}");

    // ── Services ────────────────────────────────────────────────────────────────

    public static readonly MessageTemplate ServiceNotInstalled =
        new("Service_NotInstalled", "{0} is not installed on this machine.");

    public static readonly MessageTemplate ServiceStartTimedOut =
        new("Service_StartTimedOut", "{0} did not reach the running state in time.");

    public static readonly MessageTemplate ServiceStartFailed =
        new("Service_StartFailed", "Could not start {0}: {1}");

    public static readonly MessageTemplate ServiceCannotBeStopped =
        new("Service_CannotBeStopped", "Windows does not allow {0} to be stopped while it is running.");

    public static readonly MessageTemplate ServiceStopTimedOut =
        new("Service_StopTimedOut", "{0} did not stop in time.");

    public static readonly MessageTemplate ServiceStopFailed =
        new("Service_StopFailed", "Could not stop {0}: {1}");

    public static readonly MessageTemplate ServiceUnknownStartType =
        new("Service_UnknownStartType", "Refusing to write an unknown start type.");

    public static readonly MessageTemplate ServiceSetStartTypeFailed =
        new("Service_SetStartTypeFailed", "Could not set the start type of {0}: {1}");

    // ── Power ───────────────────────────────────────────────────────────────────

    public static readonly MessageTemplate PowerNoHighPerformanceScheme =
        new(
            "Power_NoHighPerformanceScheme",
            "Neither the Ultimate Performance nor the High Performance scheme is available on this edition of Windows.");

    public static readonly MessageTemplate PowerActivateFailed =
        new("Power_ActivateFailed", "powercfg could not activate the scheme: {0}");

    public static readonly MessageTemplate PowerCoreParkingRejected =
        new("Power_CoreParkingRejected", "powercfg rejected the core parking change: {0}");

    public static readonly MessageTemplate PowerReapplyFailed =
        new("Power_ReapplyFailed", "Could not re-apply the power scheme: {0}");

    public static readonly MessageTemplate PowerHibernationFailed =
        new("Power_HibernationFailed", "powercfg could not change hibernation: {0}");

    // ── Network ─────────────────────────────────────────────────────────────────

    public static readonly MessageTemplate NetworkAdapterGone =
        new("Network_AdapterGone", "The selected network adapter is no longer available.");

    public static readonly MessageTemplate NetworkInvalidAddress =
        new("Network_InvalidAddress", "'{0}' is not a valid IPv4 address.");

    public static readonly MessageTemplate NetworkNoIpv4Interface =
        new("Network_NoIpv4Interface", "The adapter does not expose an IPv4 interface index.");

    public static readonly MessageTemplate NetworkSetResolverFailed =
        new("Network_SetResolverFailed", "netsh could not set the primary resolver: {0}");

    public static readonly MessageTemplate NetworkRestoreDhcpFailed =
        new("Network_RestoreDhcpFailed", "netsh could not restore DHCP resolvers: {0}");

    public static readonly MessageTemplate NetworkFlushCacheFailed =
        new("Network_FlushCacheFailed", "Could not flush the resolver cache: {0}");

    public static readonly MessageTemplate NetworkNoConnectedAdapter =
        new("Network_NoConnectedAdapter", "No connected network adapter was found to apply the change to.");

    // ── Privacy and the hosts file ──────────────────────────────────────────────

    public static readonly MessageTemplate HostsNeedsAdministrator =
        new("Hosts_NeedsAdministrator", "Editing the hosts file requires administrator rights.");

    public static readonly MessageTemplate HostsLocked =
        new(
            "Hosts_Locked",
            "The hosts file is locked. Real-time protection in Microsoft Defender blocks edits to it - " +
            "add an exclusion or turn tamper protection off temporarily.");

    public static readonly MessageTemplate HostsUpdateFailed =
        new("Hosts_UpdateFailed", "Could not update the hosts file: {0}");

    // ── Restore points ──────────────────────────────────────────────────────────

    public static readonly MessageTemplate RestorePointNeedsAdministrator =
        new("RestorePoint_NeedsAdministrator", "Creating a restore point requires administrator rights.");

    public static readonly MessageTemplate RestorePointProtectionOff =
        new(
            "RestorePoint_ProtectionOff",
            "System Protection is switched off for the system drive. Turn it on in " +
            "System Properties > System Protection to let SysTuneX create restore points.");

    public static readonly MessageTemplate RestorePointRefused =
        new("RestorePoint_Refused", "Windows refused to create a restore point: {0}");

    public static readonly MessageTemplate RestorePointFailed =
        new("RestorePoint_Failed", "Could not create a restore point: {0}");

    // ── Processes ───────────────────────────────────────────────────────────────

    public static readonly MessageTemplate ProcessNotRunning =
        new("Process_NotRunning", "Process {0} is no longer running.");

    public static readonly MessageTemplate ProcessSetPriorityFailed =
        new("Process_SetPriorityFailed", "Could not set priority for PID {0}: {1}");

    public static readonly MessageTemplate ProcessZeroAffinity =
        new("Process_ZeroAffinity", "An affinity mask of zero would leave the process no cores to run on.");

    public static readonly MessageTemplate ProcessSetAffinityFailed =
        new("Process_SetAffinityFailed", "Could not set affinity for PID {0}: {1}");

    // ── Boot configuration ──────────────────────────────────────────────────────

    public static readonly MessageTemplate BootNeedsAdministrator =
        new("Boot_NeedsAdministrator", "Changing the boot configuration requires administrator rights.");

    public static readonly MessageTemplate BootBcdeditRefused =
        new("Boot_BcdeditRefused", "bcdedit refused the change: {0}");

    // ── Tweaks ──────────────────────────────────────────────────────────────────

    public static readonly MessageTemplate TweakBuildGated =
        new("Tweak_BuildGated", "{0} does not apply to Windows build {1}.");

    public static readonly MessageTemplate TweakNoHandler =
        new("Tweak_NoHandler", "No handler is registered for '{0}'.");

    public static readonly MessageTemplate TweakApplyFailed =
        new("Tweak_ApplyFailed", "{0}: {1}");

    // ── Cleanup ─────────────────────────────────────────────────────────────────

    public static readonly MessageTemplate CleanupUnsafePackageName =
        new("Cleanup_UnsafePackageName", "Refusing to run with a package name that contains shell metacharacters.");

    public static readonly MessageTemplate CleanupPackageRemoveFailed =
        new("Cleanup_PackageRemoveFailed", "Could not remove {0}: {1}");

    // ── Environment ─────────────────────────────────────────────────────────────

    public static readonly MessageTemplate EnvironmentExecutableUnknown =
        new("Environment_ExecutableUnknown", "Could not resolve the SysTuneX executable path.");

    public static readonly MessageTemplate EnvironmentElevationRefused =
        new("Environment_ElevationRefused", "The elevation prompt was cancelled or blocked by policy.");

    public static readonly MessageTemplate EnvironmentExplorerRestartFailed =
        new("Environment_ExplorerRestartFailed", "Could not restart Explorer: {0}");

    // ── Game mode ───────────────────────────────────────────────────────────────

    public static readonly MessageTemplate GameModeNeedsAdministrator =
        new(
            "GameMode_NeedsAdministrator",
            "Game mode needs administrator rights to stop services and switch the power scheme.");

    public static readonly MessageTemplate GameModeAlreadyOn =
        new("GameMode_AlreadyOn", "Game mode is already on.");

    public static readonly MessageTemplate GameModeNotOn =
        new("GameMode_NotOn", "Game mode is not on.");

    // ── Journal and diagnostics ─────────────────────────────────────────────────

    public static readonly MessageTemplate BackupExportFailed =
        new("Backup_ExportFailed", "Export failed: {0}");

    public static readonly MessageTemplate DiagnosticsReportFailed =
        new("Diagnostics_ReportFailed", "Could not write the diagnostics report: {0}");

    /// <summary>Every template declared above, for the coverage test to walk.</summary>
    public static IReadOnlyList<MessageTemplate> All { get; } =
    [
        RegistryOpenFailed, RegistryValueNotKept, RegistryAccessDeniedWrite, RegistryWriteFailed,
        RegistryAccessDeniedDelete, RegistryDeleteFailed,
        ServiceNotInstalled, ServiceStartTimedOut, ServiceStartFailed, ServiceCannotBeStopped,
        ServiceStopTimedOut, ServiceStopFailed, ServiceUnknownStartType, ServiceSetStartTypeFailed,
        PowerNoHighPerformanceScheme, PowerActivateFailed, PowerCoreParkingRejected,
        PowerReapplyFailed, PowerHibernationFailed,
        NetworkAdapterGone, NetworkInvalidAddress, NetworkNoIpv4Interface, NetworkSetResolverFailed,
        NetworkRestoreDhcpFailed, NetworkFlushCacheFailed, NetworkNoConnectedAdapter,
        HostsNeedsAdministrator, HostsLocked, HostsUpdateFailed,
        RestorePointNeedsAdministrator, RestorePointProtectionOff, RestorePointRefused, RestorePointFailed,
        ProcessNotRunning, ProcessSetPriorityFailed, ProcessZeroAffinity, ProcessSetAffinityFailed,
        BootNeedsAdministrator, BootBcdeditRefused,
        TweakBuildGated, TweakNoHandler, TweakApplyFailed,
        CleanupUnsafePackageName, CleanupPackageRemoveFailed,
        EnvironmentExecutableUnknown, EnvironmentElevationRefused, EnvironmentExplorerRestartFailed,
        GameModeNeedsAdministrator, GameModeAlreadyOn, GameModeNotOn,
        BackupExportFailed, DiagnosticsReportFailed,
    ];
}
