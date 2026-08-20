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

    public double WindowWidth { get; set; } = 1240;

    public double WindowHeight { get; set; } = 800;

    public bool WindowMaximized { get; set; }
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
