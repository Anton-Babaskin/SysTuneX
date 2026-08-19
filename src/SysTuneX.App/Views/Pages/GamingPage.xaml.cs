using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class GamingPage : SysTuneXPage
{
    public GamingPage(GamingViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public GamingViewModel ViewModel { get; }
}
