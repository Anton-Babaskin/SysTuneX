using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class DashboardPage : SysTuneXPage
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public DashboardViewModel ViewModel { get; }
}
