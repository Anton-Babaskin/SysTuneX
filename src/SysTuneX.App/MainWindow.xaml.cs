using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using Wpf.Ui.Controls;
using MW = System.Windows;

namespace SysTuneX.App;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

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
}
