using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using SysTuneX.App.Views.Pages;
using SysTuneX.Core;
using SysTuneX.Core.Abstractions;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;

namespace SysTuneX.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .ConfigureServices((_, services) =>
            {
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
                $"[{DateTime.Now:u}] ({source}) {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never be the thing that takes the app down.
        }
    }

    private static void ShowFatal(Exception exception) =>
        MessageBox.Show(
            $"SysTuneX could not start.\n\n{exception.Message}",
            "SysTuneX",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
