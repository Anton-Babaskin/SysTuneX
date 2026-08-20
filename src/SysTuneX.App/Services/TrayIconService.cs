using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using SysTuneX.App.Localization;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using Forms = System.Windows.Forms;

namespace SysTuneX.App.Services;

/// <summary>Keeps a live readout and the game mode switch one click away from the taskbar.</summary>
public interface ITrayIconService : IDisposable
{
    bool IsVisible { get; }

    void Show();

    void Hide();
}

/// <inheritdoc cref="ITrayIconService"/>
public sealed class TrayIconService : ITrayIconService
{
    /// <summary>
    /// The tooltip is a glance, not a dashboard. Windows caps it at 127 characters and updating
    /// it every second would spend more time formatting than anyone spends reading it.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private const int TooltipLimit = 127;

    private readonly ILogger<TrayIconService> _logger;
    private readonly ISystemInfoService _systemInfo;
    private readonly ISensorService _sensors;
    private readonly IGameModeService _gameMode;
    private readonly ILocalizationService _localization;

    private Forms.NotifyIcon? _icon;
    private DispatcherTimer? _timer;
    private SensorReadings _readings = SensorReadings.None;
    private bool _disposed;

    public TrayIconService(
        ILogger<TrayIconService> logger,
        ISystemInfoService systemInfo,
        ISensorService sensors,
        IGameModeService gameMode,
        ILocalizationService localization)
    {
        _logger = logger;
        _systemInfo = systemInfo;
        _sensors = sensors;
        _gameMode = gameMode;
        _localization = localization;
    }

    public bool IsVisible => _icon is { Visible: true };

    public void Show()
    {
        if (_disposed)
        {
            return;
        }

        if (_icon is not null)
        {
            _icon.Visible = true;
            _timer?.Start();
            return;
        }

        try
        {
            _icon = new Forms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "SysTuneX",
                Visible = true,
                ContextMenuStrip = BuildMenu(),
            };

            _icon.DoubleClick += (_, _) => RestoreWindow();

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();

            Refresh();
            _gameMode.Changed += OnGameModeChanged;
            _localization.LanguageChanged += OnLanguageChanged;
        }
        catch (Exception ex)
        {
            // A missing tray is a lost convenience, not a reason to take the app down.
            _logger.LogWarning(ex, "The tray icon could not be created");
        }
    }

    public void Hide()
    {
        if (_icon is not null)
        {
            _icon.Visible = false;
        }

        _timer?.Stop();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        menu.Items.Add(_localization["Tray_Open"], null, (_, _) => RestoreWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(GameModeLabel(), null, (_, _) => ToggleGameMode());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_localization["Tray_Quit"], null, (_, _) => Quit());

        return menu;
    }

    private string GameModeLabel() =>
        _localization[_gameMode.IsActive ? "Tray_GameMode_Off" : "Tray_GameMode_On"];

    private void OnGameModeChanged(object? sender, EventArgs e) => RebuildMenu();

    private void OnLanguageChanged(object? sender, EventArgs e) => RebuildMenu();

    private void RebuildMenu()
    {
        if (_icon is null)
        {
            return;
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            Forms.ContextMenuStrip? previous = _icon.ContextMenuStrip;
            _icon.ContextMenuStrip = BuildMenu();
            previous?.Dispose();

            Refresh();
        });
    }

    private void ToggleGameMode() => _ = ToggleGameModeAsync();

    private async Task ToggleGameModeAsync()
    {
        try
        {
            if (_gameMode.IsActive)
            {
                await _gameMode.DisableAsync().ConfigureAwait(false);
            }
            else
            {
                await _gameMode.EnableAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game mode could not be switched from the tray");
        }
    }

    /// <summary>
    /// Builds the tooltip from whatever answered. Sensors that are not present are left out
    /// rather than shown as zero, the same rule the dashboard follows.
    /// </summary>
    private void Refresh()
    {
        if (_icon is null)
        {
            return;
        }

        try
        {
            _ = SampleSensorsAsync();

            SystemSnapshot snapshot = _systemInfo.GetSnapshot();

            var parts = new List<string>
            {
                $"CPU {snapshot.CpuUsagePercent:F0}%",
                $"RAM {snapshot.RamUsedMb / 1024.0:F1}/{snapshot.RamTotalMb / 1024.0:F1} GB",
            };

            if (_readings.Cpu is { } cpu)
            {
                parts.Add($"{_localization["Dashboard_CpuTemp"]} {cpu.Rounded}°C");
            }

            if (_readings.Gpu is { } gpu)
            {
                parts.Add($"{_localization["Dashboard_GpuTemp"]} {gpu.Rounded}°C");
            }

            if (_gameMode.IsActive)
            {
                parts.Add(_localization["GameMode_Title"]);
            }

            string text = "SysTuneX\n" + string.Join("\n", parts);
            _icon.Text = text.Length > TooltipLimit ? text[..TooltipLimit] : text;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Tray tooltip refresh failed");
        }
    }

    private async Task SampleSensorsAsync()
    {
        try
        {
            _readings = await _sensors.ReadAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            _readings = SensorReadings.None;
        }
    }

    private static void RestoreWindow() =>
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (Application.Current.MainWindow is not { } window)
            {
                return;
            }

            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        });

    private static void Quit() => Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());

    /// <summary>Reads the icon compiled into the assembly rather than a file beside the exe.</summary>
    private static Icon LoadIcon()
    {
        var uri = new Uri("pack://application:,,,/SysTuneX;component/Assets/SysTuneX.ico", UriKind.Absolute);

        using Stream stream = Application.GetResourceStream(uri)?.Stream
            ?? throw new FileNotFoundException("SysTuneX.ico is not embedded in the assembly.");

        return new Icon(stream);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gameMode.Changed -= OnGameModeChanged;
        _localization.LanguageChanged -= OnLanguageChanged;

        _timer?.Stop();
        _timer = null;

        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Dispose();
            _icon = null;
        }
    }
}
