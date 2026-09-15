using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using SysTuneX.Core.Models;

namespace SysTuneX.App.Services;

/// <summary>Opens and closes the small always-on-top readout, and owns the key that summons it.</summary>
public interface ICompactMonitorService
{
    bool IsOpen { get; }

    /// <summary>Why the key is not listening, or <see cref="HotkeyFailure.None"/> when it is.</summary>
    HotkeyFailure HotkeyFailure { get; }

    /// <summary>The Win32 error behind a <see cref="HotkeyFailure.Refused"/>, and zero otherwise.</summary>
    int HotkeyErrorCode { get; }

    /// <summary>Raised when it opens or closes, or when the key's state changes.</summary>
    event EventHandler? StateChanged;

    void Show();

    void Hide();

    void Toggle();

    /// <summary>
    /// Hangs the hotkey on <paramref name="owner"/>. Called once the main window has a handle;
    /// a global hotkey belongs to a window and there is nothing to attach it to before that.
    /// </summary>
    void AttachHotkey(Window owner);

    /// <summary>Re-reads the configured key and asks Windows again, after the user changed it.</summary>
    void ReapplyHotkey();
}

/// <summary>
/// One compact window at a time, built on demand and thrown away when it closes.
///
/// Created through the container rather than held as a singleton: the window's view model runs a
/// timer and samples sensors, and a singleton would keep both alive for the life of the app even
/// with the window closed - which is exactly the kind of background work this app exists to remove
/// from a machine, and it would be embarrassing to add it here.
/// </summary>
public sealed class CompactMonitorService : ICompactMonitorService
{
    private readonly IServiceProvider _services;
    private readonly IGlobalHotkeyService _hotkey;
    private readonly IAppSettingsService _settings;
    private readonly ILogger<CompactMonitorService> _logger;

    private CompactMonitorWindow? _window;
    private Window? _hotkeyOwner;

    public CompactMonitorService(
        IServiceProvider services,
        IGlobalHotkeyService hotkey,
        IAppSettingsService settings,
        ILogger<CompactMonitorService> logger)
    {
        _services = services;
        _hotkey = hotkey;
        _settings = settings;
        _logger = logger;

        _hotkey.Pressed += (_, _) => Toggle();
    }

    public bool IsOpen => _window is not null;

    public HotkeyFailure HotkeyFailure => _hotkey.Failure;

    public int HotkeyErrorCode => _hotkey.ErrorCode;

    public event EventHandler? StateChanged;

    public void AttachHotkey(Window owner)
    {
        _hotkeyOwner = owner;
        ReapplyHotkey();
    }

    public void ReapplyHotkey()
    {
        if (_hotkeyOwner is null)
        {
            return;
        }

        _hotkey.Unregister();

        CompactMonitorSettings compact = _settings.Current.CompactMonitor;
        if (compact.HotkeyEnabled)
        {
            // A refusal is kept rather than thrown: TryParse always hands back a usable spec, and
            // the settings page shows whatever Windows said about it.
            HotkeySpec.TryParse(compact.Hotkey, out HotkeySpec spec);
            _hotkey.Register(_hotkeyOwner, spec);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Show()
    {
        if (_window is not null)
        {
            // Already open. Bring it forward rather than making a second one: Topmost loses to
            // another topmost window, and a full-screen game is one.
            _window.Activate();
            return;
        }

        try
        {
            CompactMonitorWindow window = _services.GetRequiredService<CompactMonitorWindow>();

            window.Closed += OnClosed;
            _window = window;

            window.Show();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // A readout that will not open is a lost convenience, not a reason to take the app
            // down - and the main window is where the same numbers already are.
            _logger.LogError(ex, "The compact monitor could not be opened");
            _window = null;
        }
    }

    public void Hide() => _window?.Close();

    public void Toggle()
    {
        if (IsOpen)
        {
            Hide();
        }
        else
        {
            Show();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is CompactMonitorWindow window)
        {
            window.Closed -= OnClosed;
            window.ViewModel.Dispose();
        }

        _window = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
