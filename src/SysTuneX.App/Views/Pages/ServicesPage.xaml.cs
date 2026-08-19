using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class ServicesPage : SysTuneXPage
{
    public ServicesPage(ServicesViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public ServicesViewModel ViewModel { get; }
}
