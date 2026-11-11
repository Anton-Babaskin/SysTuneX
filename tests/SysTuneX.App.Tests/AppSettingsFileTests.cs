using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.App.Services;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Xunit;

namespace SysTuneX.App.Tests;

/// <summary>
/// settings.json moved onto <c>JsonFile</c> with the other stores. It is the one file people are
/// told they can edit by hand, so the two things that matter most are that the file they already
/// have still loads, and that a typo in it is kept for them to fix rather than overwritten.
///
/// Here rather than in the core tests because the settings carry WPF UI's theme and backdrop types.
/// </summary>
public sealed class AppSettingsFileTests : IDisposable
{
    /// <summary>Written out by hand, in the shape the previous version saved: enums as names.</summary>
    private const string SettingsFile = """
        {
          "Language": "ru",
          "Theme": "Dark",
          "Backdrop": "Acrylic",
          "CreateRestorePointBeforeProfiles": false,
          "ConfirmAdvancedChanges": true,
          "VerboseLogging": true,
          "AutoGameMode": true,
          "ShowTrayIcon": true,
          "MinimizeToTray": true,
          "Schedule": {
            "Enabled": true,
            "StartsAt": "20:30:00",
            "EndsAt": "01:00:00",
            "Days": [
              "Friday",
              "Saturday"
            ]
          },
          "Monitor": {
            "FrameCounter": true,
            "KeepFrameCounterInBackground": false,
            "Metrics": [
              "CpuLoad",
              "GpuTemperature"
            ]
          },
          "CompactMonitor": {
            "Hotkey": "Ctrl+Alt+F",
            "HotkeyEnabled": true,
            "Left": 1610,
            "Top": 24,
            "Opacity": 0.75,
            "OpenOnStartup": true
          },
          "WindowWidth": 1400,
          "WindowHeight": 900,
          "WindowMaximized": false
        }
        """;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "systunex-settings-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public async Task Settings_from_the_previous_version_still_load()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, SettingsFile);

        AppSettingsService service = Service(NullLogger<AppSettingsService>.Instance);
        await service.LoadAsync();
        AppSettings settings = service.Current;

        Assert.Equal("ru", settings.Language);
        Assert.Equal(ApplicationTheme.Dark, settings.Theme);
        Assert.Equal(WindowBackdropType.Acrylic, settings.Backdrop);
        Assert.False(settings.CreateRestorePointBeforeProfiles);
        Assert.True(settings.MinimizeToTray);
        Assert.Equal(new TimeOnly(20, 30), settings.Schedule.StartsAt);
        Assert.Equal([DayOfWeek.Friday, DayOfWeek.Saturday], settings.Schedule.Days);
        Assert.Equal(["CpuLoad", "GpuTemperature"], settings.Monitor.Metrics);
        Assert.Equal("Ctrl+Alt+F", settings.CompactMonitor.Hotkey);
        Assert.Equal(0.75, settings.CompactMonitor.Opacity);
        Assert.Equal(1400, settings.WindowWidth);
    }

    [Fact]
    public async Task A_settings_file_that_does_not_parse_is_kept_and_the_defaults_are_used()
    {
        Directory.CreateDirectory(_directory);
        string broken = SettingsFile.Replace("\"Dark\",", "\"Dark\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(SettingsPath, broken);
        var logger = new ErrorCounter();

        AppSettingsService service = Service(logger);
        await service.LoadAsync();

        Assert.Equal(new AppSettings().Theme, service.Current.Theme);
        Assert.Equal(string.Empty, service.Current.Language);
        Assert.False(File.Exists(SettingsPath));
        string corrupt = Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt-*"));
        Assert.Equal(broken, await File.ReadAllTextAsync(corrupt));
        Assert.Equal(1, logger.Errors);
    }

    /// <summary>
    /// Starts from a saved file rather than from the defaults. The defaults hold a readout that has
    /// never been positioned, as NaN, and these serializer options cannot write NaN - a separate
    /// problem, and one this test should not depend on either way.
    /// </summary>
    [Fact]
    public async Task Saving_leaves_only_the_settings_file()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, SettingsFile);

        AppSettingsService service = Service(NullLogger<AppSettingsService>.Instance);
        await service.LoadAsync();
        service.Current.Language = "uk";
        await service.SaveAsync();

        Assert.Equal(["settings.json"], new DirectoryInfo(_directory).GetFiles().Select(f => f.Name));

        AppSettingsService afterRestart = Service(NullLogger<AppSettingsService>.Instance);
        await afterRestart.LoadAsync();
        Assert.Equal("uk", afterRestart.Current.Language);
        Assert.Equal(ApplicationTheme.Dark, afterRestart.Current.Theme);
    }

    private AppSettingsService Service(ILogger<AppSettingsService> logger) =>
        new(logger, new FakeEnvironment(_directory));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // A leftover temp folder is not worth failing a test run over.
        }
    }

    private sealed class ErrorCounter : ILogger<AppSettingsService>
    {
        public int Errors { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors++;
            }
        }
    }

    /// <summary>Only the data directory is read; nothing here needs the rest of a machine.</summary>
    private sealed class FakeEnvironment(string dataDirectory) : IEnvironmentService
    {
        public bool IsElevated => true;

        public WindowsVersionInfo Windows { get; } = new()
        {
            Major = 10,
            Minor = 0,
            Build = 26100,
            ProductName = "Windows 11 Pro",
        };

        public string DataDirectory { get; } = dataDirectory;

        public OperationResult RestartElevated() => OperationResult.Ok();

        public Task<OperationResult> RestartExplorerAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> RestartWindowsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());
    }
}
