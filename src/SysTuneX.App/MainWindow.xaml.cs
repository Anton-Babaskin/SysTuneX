using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using System.Windows;
using Wpf.Ui.Controls;

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
            MessageBox.Show($"MainWindow XAML error:\n\n{ex}", "SysTuneX",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Navigation error:\n\n{ex}", "SysTuneX",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        App.Log("MainWindow constructor done");
    }
}
