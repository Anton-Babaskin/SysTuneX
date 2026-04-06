using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using SysTuneX.Core.Services;

namespace SysTuneX.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
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

                // Views
                services.AddSingleton<MainWindow>();
                services.AddTransient<DashboardPage>();
                services.AddTransient<GamingPage>();
                services.AddTransient<ServicesPage>();
                services.AddTransient<PrivacyPage>();
                services.AddTransient<NetworkPage>();
                services.AddTransient<CleanupPage>();
                services.AddTransient<ProfilesPage>();
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
