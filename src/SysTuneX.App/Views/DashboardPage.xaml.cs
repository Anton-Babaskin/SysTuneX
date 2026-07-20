using System.Windows.Controls;
using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views;

public partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Initialize();
        Unloaded += (_, _) => ViewModel.StopMonitoring();
    }
}
