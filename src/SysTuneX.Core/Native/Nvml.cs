using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SysTuneX.Core.Native;

/// <summary>
/// NVIDIA Management Library, the documented way to read a GeForce card's temperature.
///
/// nvml.dll ships with the display driver and lives in System32, so this needs no install and
/// no kernel driver — which matters: the usual "hardware monitor" route (WinRing0 and friends)
/// loads a signed-but-vulnerable kernel driver that Microsoft's driver blocklist and several
/// anti-cheats both object to. Not a trade worth making in a tool aimed at gamers.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Nvml
{
    private const string Library = "nvml.dll";

    /// <summary>NVML_TEMPERATURE_GPU — the only sensor type consumer cards expose.</summary>
    internal const uint TemperatureSensorGpu = 0;

    internal const int Success = 0;

    [DllImport(Library, EntryPoint = "nvmlInit_v2")]
    internal static extern int Init();

    [DllImport(Library, EntryPoint = "nvmlShutdown")]
    internal static extern int Shutdown();

    [DllImport(Library, EntryPoint = "nvmlDeviceGetCount_v2")]
    internal static extern int GetDeviceCount(out uint count);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    internal static extern int GetDeviceHandle(uint index, out IntPtr device);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetTemperature")]
    internal static extern int GetTemperature(IntPtr device, uint sensorType, out uint celsius);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetUtilizationRates")]
    internal static extern int GetUtilization(IntPtr device, out Utilization utilization);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetFanSpeed")]
    internal static extern int GetFanSpeed(IntPtr device, out uint percent);

    [DllImport(Library, EntryPoint = "nvmlDeviceGetName", CharSet = CharSet.Ansi)]
    internal static extern int GetName(IntPtr device, byte[] name, uint length);

    /// <summary>Percentage of the last sampling period each engine was busy.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Utilization
    {
        internal uint Gpu;
        internal uint Memory;
    }
}
