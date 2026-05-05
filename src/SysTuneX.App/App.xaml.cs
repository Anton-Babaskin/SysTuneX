using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using SysTuneX.Core.Services;
using Wpf.Ui.Appearance;
using Application = System.Windows.Application;

namespace SysTuneX.App;

public partial class App : Application
{
    private readonly IHost _host;
    private NotifyIcon? _trayIcon;

    private static readonly string _log = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SysTuneX_debug.log");

    public static void Log(string msg)
    {
        try { File.AppendAllText(_log, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
    }

    static App()
    {
        try
        {
            File.WriteAllText(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "SysTuneX_debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] App class loaded\n");
        }
        catch { }
    }

    public App()
    {
        Log("App() constructor start");

        DispatcherUnhandledException += (_, args) =>
        {
            Log($"DispatcherUnhandledException: {args.Exception}");
            System.Windows.MessageBox.Show($"Unhandled error:\n\n{args.Exception}", "SysTuneX Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log($"AppDomain.UnhandledException: {args.ExceptionObject}");
            try
            {
                System.Windows.MessageBox.Show($"Fatal error:\n\n{args.ExceptionObject}", "SysTuneX Fatal Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
        };

        Log("Exception handlers registered");

        try
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
                    services.AddSingleton<IRestorePointService, RestorePointService>();
                    services.AddSingleton<IStartupService, StartupService>();
                    services.AddSingleton<IGameDetectionService, GameDetectionService>();

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
                    services.AddTransient<StartupViewModel>();

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
                    services.AddTransient<StartupPage>();
                })
                .Build();

            Log("Host built successfully");
        }
        catch (Exception ex)
        {
            Log($"CRASH in host.Build(): {ex}");
            System.Windows.MessageBox.Show($"Failed to initialize app:\n\n{ex}", "SysTuneX",
                MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        Log("OnStartup called");
        try
        {
            ApplicationThemeManager.Apply(LoadThemePreference());

            await _host.StartAsync();
            Log("Host started");

            _host.Services.GetRequiredService<IGameDetectionService>().Start();
            Log("GameDetectionService started");

            InitTrayIcon();
            Log("Tray icon initialized");

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            Log("MainWindow resolved from DI");

            mainWindow.Show();
            Log("mainWindow.Show() called");

            base.OnStartup(e);
            Log("base.OnStartup done");
        }
        catch (Exception ex)
        {
            Log($"OnStartup CRASH: {ex}");
            System.Windows.MessageBox.Show($"Startup error:\n\n{ex}", "SysTuneX",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log("OnExit called");
        _trayIcon?.Dispose();
        _host.Services.GetRequiredService<IGameDetectionService>().Stop();
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }

    private void InitTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open SysTuneX", null, (_, _) => ShowMainWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "SysTuneX — Game Optimizer",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }

    private void ExitApp()
    {
        _host.Services.GetRequiredService<MainWindow>().ForceClose();
        Shutdown(0);
    }

    public static T GetService<T>() where T : class
    {
        var app = (App)Current;
        return app._host.Services.GetRequiredService<T>();
    }

    private static ApplicationTheme LoadThemePreference()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\SysTuneX");
            return key?.GetValue("Theme")?.ToString() == "Light"
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
        }
        catch { return ApplicationTheme.Dark; }
    }

    public static void SaveThemePreference(ApplicationTheme theme)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser
                .CreateSubKey(@"SOFTWARE\SysTuneX", writable: true);
            key?.SetValue("Theme", theme.ToString());
        }
        catch { }
    }
}
