using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SysTuneX.App.Services;
using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using SysTuneX.Core.Services;
using Wpf.Ui;

namespace SysTuneX.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Unhandled error:\n\n{args.Exception}", "SysTuneX Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            MessageBox.Show($"Fatal error:\n\n{args.ExceptionObject}", "SysTuneX Fatal Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Navigation
                services.AddSingleton<IPageService>(sp => new PageService(sp));
                services.AddSingleton<INavigationService, NavigationService>();

                // Core services
                services.AddSingleton<IRegistryService, RegistryService>();
                services.AddSingleton<IServiceManager, ServiceManager>();
                services.AddSingleton<ISystemInfoService, SystemInfoService>();
                services.AddSingleton<IProcessService, ProcessService>();
                services.AddSingleton<IPowerService, PowerService>();
                services.AddSingleton<IPrivacyService, PrivacyService>();
                services.AddSingleton<INetworkService, NetworkService>();
                services.AddSingleton<ICleanupService, CleanupService>();

                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<GamingViewModel>();
                services.AddTransient<ServicesViewModel>();
                services.AddTransient<PrivacyViewModel>();
                services.AddTransient<NetworkViewModel>();
                services.AddTransient<CleanupViewModel>();
                services.AddTransient<ProfilesViewModel>();
                services.AddTransient<Windows11ViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
                services.AddTransient<DashboardPage>();
                services.AddTransient<GamingPage>();
                services.AddTransient<ServicesPage>();
                services.AddTransient<PrivacyPage>();
                services.AddTransient<NetworkPage>();
                services.AddTransient<CleanupPage>();
                services.AddTransient<ProfilesPage>();
                services.AddTransient<Windows11Page>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._host.Services.GetRequiredService<T>();
    }
}
