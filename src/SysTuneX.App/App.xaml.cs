using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SysTuneX.App.Diagnostics;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using SysTuneX.App.Views.Pages;
using SysTuneX.Core;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;

namespace SysTuneX.App;

public partial class App : Application
{
    private readonly IHost _host;

    // Shared with the container so the settings page can turn verbose logging on without a restart.
    private readonly LogLevelSwitch _logLevel = new();

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();

                // Every service in Core logs through ILogger, but until now the only provider was
                // Debug - so on a user's machine all of it went nowhere. This is the sink that
                // makes a bug report possible without a debugger attached.
                logging.AddProvider(new FileLoggerProvider(_logLevel));

                // The switch, not a fixed level: the file provider decides per message.
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices((_, services) =>
            {
                // Registered before Core, which only fills in a default when nothing is there.
                services.AddSingleton(_logLevel);
                services.AddSysTuneXCore();

                // The markup extension resolves strings through a static, so the container
                // must hand out that same instance rather than a second one.
                services.AddSingleton(_ => LocalizationService.Instance);
                services.AddSingleton<CatalogText>();
                services.AddSingleton<IAppSettingsService, AppSettingsService>();
                services.AddSingleton<INavigationViewPageProvider, PageProvider>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<ISnackbarService, SnackbarService>();
                services.AddSingleton<IContentDialogService, ContentDialogService>();
                services.AddSingleton<IUserInteraction, UserInteraction>();

                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainWindowViewModel>();

                // Pages and their view models are singletons because NavigationCacheMode keeps the
                // page alive anyway. Making them transient, as the old build did, created a fresh
                // view model and a fresh refresh timer on every single navigation.
                AddPage<DashboardPage, DashboardViewModel>(services);
                AddPage<ProfilesPage, ProfilesViewModel>(services);
                AddPage<GamingPage, GamingViewModel>(services);
                AddPage<Windows11Page, Windows11ViewModel>(services);
                AddPage<ServicesPage, ServicesViewModel>(services);
                AddPage<PrivacyPage, PrivacyViewModel>(services);
                AddPage<NetworkPage, NetworkViewModel>(services);
                AddPage<CleanupPage, CleanupViewModel>(services);
                AddPage<HistoryPage, HistoryViewModel>(services);
                AddPage<SettingsPage, SettingsViewModel>(services);
            })
            .Build();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static IServiceProvider Services => ((App)Current)._host.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            await _host.StartAsync();

            LogSessionStart();

            var localization = _host.Services.GetRequiredService<ILocalizationService>();

            var settings = _host.Services.GetRequiredService<IAppSettingsService>();
            await settings.LoadAsync();
            localization.SetLanguage(settings.Current.Language);

            ApplyTheme(settings.Current.Theme);

            // Load the journal up front so tweak pages can show what is already recorded.
            await _host.Services.GetRequiredService<IBackupService>().LoadAsync();

            MainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            Log(ex, "startup");
            ShowFatal(ex);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            await _host.Services.GetRequiredService<IAppSettingsService>().SaveAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        catch
        {
            // Shutdown must not throw; there is nothing left to report to.
        }

        base.OnExit(e);
    }

    /// <summary>
    /// First lines of every session. Without them a log is a pile of messages with no way to tell
    /// which build produced it or whether the process even had the rights to do anything.
    /// </summary>
    private void LogSessionStart()
    {
        try
        {
            var logger = _host.Services.GetRequiredService<ILogger<App>>();
            var environment = _host.Services.GetRequiredService<IEnvironmentService>();

            logger.LogInformation(
                "SysTuneX {Version} starting - {Windows}, elevated={Elevated}, supported={Supported}",
                typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown",
                environment.Windows,
                environment.IsElevated,
                environment.Windows.IsSupported);
        }
        catch (Exception ex)
        {
            Log(ex, "startup-banner");
        }
    }

    private static void ApplyTheme(ApplicationTheme theme)
    {
        // Unknown means "follow Windows", which is what a fresh install gets. The watcher that
        // reacts to later theme changes needs a window handle, so MainWindow starts it instead.
        if (theme == ApplicationTheme.Unknown)
        {
            ApplicationThemeManager.ApplySystemTheme();
        }
        else
        {
            ApplicationThemeManager.Apply(theme);
        }
    }

    private static void AddPage<TPage, TViewModel>(IServiceCollection services)
        where TPage : class
        where TViewModel : class
    {
        services.AddSingleton<TViewModel>();
        services.AddSingleton<TPage>();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception, "dispatcher");

        // A failed page action should leave the user in the app, not drop them at the desktop.
        e.Handled = true;

        try
        {
            _host.Services.GetRequiredService<IUserInteraction>().ShowError(e.Exception.Message);
        }
        catch
        {
            ShowFatal(e.Exception);
        }
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log(exception, "domain");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log(e.Exception, "task");
        e.SetObserved();
    }

    private void Log(Exception exception, string source)
    {
        try
        {
            var logger = _host.Services.GetService<ILogger<App>>();
            logger?.LogError(exception, "Unhandled {Source} exception", source);

            var environment = _host.Services.GetService<IEnvironmentService>();
            if (environment is null)
            {
                return;
            }

            Directory.CreateDirectory(environment.DataDirectory);
            File.AppendAllText(
                Path.Combine(environment.DataDirectory, "errors.log"),
                $"[{DateTime.Now:u}] ({source}) {ExceptionReport.Describe(exception)}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never be the thing that takes the app down.
        }
    }

    private void ShowFatal(Exception exception)
    {
        // The outer message of a startup failure is almost always useless on its own - WPF's
        // "Provide value on 'TypeConverterMarkupExtension' threw an exception" names nothing.
        // Lead with the innermost cause and print the whole chain underneath, so a screenshot
        // of this dialog is enough to find the fault.
        var root = ExceptionReport.RootCause(exception);
        var text = new StringBuilder()
            .AppendLine("SysTuneX could not start.")
            .AppendLine()
            .AppendLine(root.Message)
            .AppendLine()
            .AppendLine("Details:")
            .AppendLine(ExceptionReport.Describe(exception));

        if (LogPath() is { } path)
        {
            text.AppendLine().Append("A full report was written to ").Append(path).AppendLine(".");
        }

        MessageBox.Show(text.ToString(), "SysTuneX", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private string? LogPath()
    {
        try
        {
            var environment = _host.Services.GetService<IEnvironmentService>();
            return environment is null ? null : Path.Combine(environment.DataDirectory, "errors.log");
        }
        catch
        {
            return null;
        }
    }
}
