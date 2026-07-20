using SysTuneX.App.ViewModels;
using SysTuneX.App.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SysTuneX.App;

public partial class MainWindow : FluentWindow
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        Loaded += (_, _) =>
        {
            RootNavigation.Navigate(typeof(DashboardPage));
        };
    }
}
