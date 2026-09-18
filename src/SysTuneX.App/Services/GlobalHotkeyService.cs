using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Models;

namespace SysTuneX.App.Services;

/// <summary>A hotkey that works while another application has focus.</summary>
public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>True once Windows has accepted the combination.</summary>
    bool IsRegistered { get; }

    /// <summary>
    /// Why it is not registered, as a code the interface can translate. A hotkey that silently
    /// does nothing is indistinguishable from a broken app, and the usual cause - another program
    /// got there first - is something the user can actually act on.
    /// </summary>
    HotkeyFailure Failure { get; }

    /// <summary>The Win32 error behind <see cref="HotkeyFailure.Refused"/>, and zero otherwise.</summary>
    int ErrorCode { get; }

    event EventHandler? Pressed;

    /// <summary>Registers <paramref name="spec"/>, replacing whatever was registered before.</summary>
    void Register(Window owner, HotkeySpec spec);

    void Unregister();
}

/// <inheritdoc cref="IGlobalHotkeyService"/>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WmHotkey = 0x0312;

    /// <summary>Any id unique within this window; the window is ours, so one constant is enough.</summary>
    private const int HotkeyId = 0xA17E;

    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED.</summary>
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly ILogger<GlobalHotkeyService> _logger;

    private HwndSource? _source;
    private bool _registered;

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger) => _logger = logger;

    public bool IsRegistered => _registered;

    public HotkeyFailure Failure { get; private set; }

    public int ErrorCode { get; private set; }

    public event EventHandler? Pressed;

    // DllImport rather than the newer LibraryImport, which generates code that needs
    // AllowUnsafeBlocks. Core turns that on because its sensor structs genuinely need it; turning
    // it on for the interface project to save two lines of marshalling would be the tail wagging
    // the dog.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    public void Register(Window owner, HotkeySpec spec)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(spec);

        Unregister();

        nint handle = new WindowInteropHelper(owner).Handle;
        if (handle == 0)
        {
            // Before the window has a handle there is nothing to hang a hotkey on. The caller
            // registers from Loaded, so this is a programming error rather than a user-facing one.
            Failure = HotkeyFailure.WindowNotReady;
            return;
        }

        if (!TryVirtualKey(spec.Key, out uint virtualKey))
        {
            Failure = HotkeyFailure.UnknownKey;
            _logger.LogWarning("Hotkey {Hotkey} names a key that does not exist", spec);
            return;
        }

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(OnMessage);

        if (!RegisterHotKey(handle, HotkeyId, (uint)spec.Modifiers, virtualKey))
        {
            int error = Marshal.GetLastWin32Error();

            // 1409 is ERROR_HOTKEY_ALREADY_REGISTERED: some other program owns the combination.
            // It is by far the most likely failure and the only one the user can do anything
            // about, so it is told apart from every other refusal.
            Failure = error == ErrorHotkeyAlreadyRegistered ? HotkeyFailure.AlreadyTaken : HotkeyFailure.Refused;
            ErrorCode = error;

            _logger.LogWarning(
                "Could not register the hotkey {Hotkey}: {Failure} (error {Error})", spec, Failure, error);

            _source?.RemoveHook(OnMessage);
            _source = null;
            return;
        }

        _registered = true;
        Failure = HotkeyFailure.None;
        ErrorCode = 0;
        _logger.LogInformation("Global hotkey {Hotkey} registered", spec);
    }

    public void Unregister()
    {
        if (_source is null)
        {
            return;
        }

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(OnMessage);
        _source = null;
        Failure = HotkeyFailure.None;
        ErrorCode = 0;
    }

    public void Dispose() => Unregister();

    private nint OnMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    /// <summary>
    /// Turns "M", "F8" or "OEM3" into the virtual key code Windows wants.
    ///
    /// Goes through WPF's own Key enum rather than a hand-written table, so every name WPF can
    /// show in the interface is a name this accepts - a table would drift the first time somebody
    /// added a key to one and not the other.
    /// </summary>
    private static bool TryVirtualKey(string name, out uint virtualKey)
    {
        virtualKey = 0;

        if (!Enum.TryParse(name, ignoreCase: true, out Key key) || key == Key.None)
        {
            return false;
        }

        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }
}
