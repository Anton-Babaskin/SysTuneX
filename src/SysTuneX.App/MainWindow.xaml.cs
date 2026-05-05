using System.ComponentModel;
using System.Windows;
using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MW = System.Windows;

namespace SysTuneX.App;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    private bool _allowClose = false;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        App.Log("MainWindow constructor start");
        ViewModel = viewModel;
        DataContext = this;

        try
        {
            InitializeComponent();
            App.Log("MainWindow.InitializeComponent() OK");
        }
        catch (Exception ex)
        {
            App.Log($"MainWindow.InitializeComponent() CRASH: {ex}");
            MW.MessageBox.Show($"MainWindow XAML error:\n\n{ex}", "SysTuneX",
                MW.MessageBoxButton.OK, MW.MessageBoxImage.Error);
            throw;
        }

        try
        {
            RootNavigation.SetServiceProvider(serviceProvider);
            App.Log("SetServiceProvider OK");
        }
        catch (Exception ex)
        {
            App.Log($"SetServiceProvider CRASH: {ex}");
        }

        UpdateThemeIcon(ApplicationThemeManager.GetAppTheme());

        Loaded += (_, _) =>
        {
            App.Log("MainWindow Loaded — calling Navigate(DashboardPage)");
            try
            {
                RootNavigation.Navigate(typeof(DashboardPage));
                App.Log("Navigate(DashboardPage) OK");
            }
            catch (Exception ex)
            {
                App.Log($"Navigate CRASH: {ex}");
                MW.MessageBox.Show($"Navigation error:\n\n{ex}", "SysTuneX",
                    MW.MessageBoxButton.OK, MW.MessageBoxImage.Error);
            }
        };

        App.Log("MainWindow constructor done");
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var current = ApplicationThemeManager.GetAppTheme();
        var next = current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(next);
        App.SaveThemePreference(next);
        UpdateThemeIcon(next);
    }

    private void UpdateThemeIcon(ApplicationTheme theme)
    {
        ThemeIcon.Symbol = theme == ApplicationTheme.Dark
            ? SymbolRegular.WeatherSunny24
            : SymbolRegular.WeatherMoon24;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }
}
