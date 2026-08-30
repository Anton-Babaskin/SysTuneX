using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SysTuneX.App.Services;

public sealed class AppSettings
{
    /// <summary>Empty means "follow Windows".</summary>
    public string Language { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ApplicationTheme Theme { get; set; } = ApplicationTheme.Unknown;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WindowBackdropType Backdrop { get; set; } = WindowBackdropType.Mica;

    public bool CreateRestorePointBeforeProfiles { get; set; } = true;

    public bool ConfirmAdvancedChanges { get; set; } = true;

    /// <summary>Records every registry read and command line, not just outcomes. Off by default.</summary>
    public bool VerboseLogging { get; set; }

    /// <summary>Turn game mode on and off automatically as a watched game starts and exits.</summary>
    public bool AutoGameMode { get; set; }

    /// <summary>Show the tray icon with live counters and the game mode switch.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Closing the window leaves SysTuneX in the tray instead of exiting.</summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>Window of the day during which game mode is held on.</summary>
    public GameModeSchedule Schedule { get; set; } = new();

    /// <summary>What the monitor page measures and shows.</summary>
    public MonitorSettings Monitor { get; set; } = new();

    public double WindowWidth { get; set; } = 1240;

    public double WindowHeight { get; set; } = 800;

    public bool WindowMaximized { get; set; }
}

/// <summary>
/// Which readings the monitor takes, and whether it takes them at all.
///
/// Every one of these is a real cost, not a preference about clutter. The frame counter holds an
/// Event Tracing session open for as long as it runs, and that is a machine-wide resource paid for
/// by the whole system - so it is off until asked for, rather than started quietly the first time
/// someone opens the page. Temperatures cost a WMI query or a call into the vendor's driver
/// library. A tuning tool that leaves all of that running for numbers nobody is reading is doing
/// the opposite of its job.
/// </summary>
public sealed class MonitorSettings
{
    /// <summary>
    /// The frame counter. Off by default: it needs administrator rights and a trace session, and
    /// it should be a deliberate act rather than something that happens on a page visit.
    /// </summary>
    public bool FrameCounter { get; set; }

    /// <summary>Keep the counter running after leaving the page, so a game can be measured while it is in front.</summary>
    public bool KeepFrameCounterInBackground { get; set; } = true;

    public bool ShowCpuLoad { get; set; } = true;

    public bool ShowCpuTemperature { get; set; } = true;

    public bool ShowGpuLoad { get; set; } = true;

    public bool ShowGpuTemperature { get; set; } = true;

    public bool ShowGpuFan { get; set; }

    public bool ShowMemory { get; set; } = true;

    public bool ShowProcessCount { get; set; }

    /// <summary>True when any reading that needs a sensor is switched on, so the slow sample can be skipped entirely.</summary>
    [JsonIgnore]
    public bool NeedsSensors => ShowCpuTemperature || ShowGpuLoad || ShowGpuTemperature || ShowGpuFan;
}

public interface IAppSettingsService
{
    AppSettings Current { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Settings persisted next to the backup journal, so both survive a reinstall of the app.</summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<AppSettingsService> _logger;
    private readonly IEnvironmentService _environment;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AppSettingsService(ILogger<AppSettingsService> logger, IEnvironmentService environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public AppSettings Current { get; private set; } = new();

    private string SettingsPath => Path.Combine(_environment.DataDirectory, "settings.json");

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            await using FileStream stream = File.OpenRead(SettingsPath);
            AppSettings? loaded = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (loaded is not null)
            {
                Current = loaded;
            }
        }
        catch (Exception ex)
        {
            // Bad settings must never stop the app; the defaults are perfectly usable.
            _logger.LogWarning(ex, "Could not read settings, using defaults");
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_environment.DataDirectory);

            await using FileStream stream = File.Create(SettingsPath);
            await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist settings");
        }
        finally
        {
            _gate.Release();
        }
    }
}
