using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SysTuneX.Core.Native;

/// <summary>
/// AMD's Display Library, the documented user-mode way to read a Radeon's temperature.
///
/// Only the scalar entry points are bound here - integers in, integers out. ADL's richer calls
/// take large structs with fixed character arrays whose layout differs between driver branches,
/// and getting one of those wrong corrupts memory rather than returning an error code. A tuner
/// that can crash the machine it is tuning is worse than one that shows no temperature, so this
/// deliberately stays with the calls that cannot.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Adl
{
    private const string Library = "atiadlxx.dll";

    internal const int Ok = 0;

    /// <summary>ADL reports temperature in thousandths of a degree.</summary>
    internal const double MilliDegrees = 1000.0;

    /// <summary>ADLOD8 / OverdriveN edge temperature, the sensor consumer tools show.</summary>
    internal const int TemperatureTypeCore = 1;

    /// <summary>ADL allocates through the caller. Returning zeroed memory keeps its structs sane.</summary>
    internal delegate IntPtr MallocCallback(int size);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL2_Main_Control_Create")]
    internal static extern int MainControlCreate(MallocCallback callback, int enumConnectedAdapters, out IntPtr context);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL2_Main_Control_Destroy")]
    internal static extern int MainControlDestroy(IntPtr context);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL2_Adapter_NumberOfAdapters_Get")]
    internal static extern int NumberOfAdapters(IntPtr context, out int count);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL2_Adapter_Active_Get")]
    internal static extern int AdapterActive(IntPtr context, int adapterIndex, out int active);

    /// <summary>Which Overdrive generation this adapter speaks, which decides the temperature call.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL2_Overdrive_Caps")]
    internal static extern int OverdriveCaps(IntPtr context, int adapterIndex, out int supported, out int enabled, out int version);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL2_OverdriveN_Temperature_Get")]
    internal static extern int OverdriveNTemperature(IntPtr context, int adapterIndex, int temperatureType, out int milliDegrees);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL2_Overdrive6_Temperature_Get")]
    internal static extern int Overdrive6Temperature(IntPtr context, int adapterIndex, out int milliDegrees);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ADL2_Overdrive6_FanSpeed_Get")]
    internal static extern int Overdrive6FanSpeed(IntPtr context, int adapterIndex, out int fanSpeed);
}
