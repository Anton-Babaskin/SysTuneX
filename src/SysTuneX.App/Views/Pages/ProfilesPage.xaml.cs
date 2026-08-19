using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views.Pages;

public partial class ProfilesPage : SysTuneXPage
{
    public ProfilesPage(ProfilesViewModel viewModel)
    {
        ViewModel = viewModel;
        Bind(viewModel);
        InitializeComponent();
    }

    public ProfilesViewModel ViewModel { get; }
}
