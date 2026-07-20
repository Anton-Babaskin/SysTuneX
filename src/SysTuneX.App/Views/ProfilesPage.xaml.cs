using System.Windows.Controls;
using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views;

public partial class ProfilesPage : Page
{
    public ProfilesViewModel ViewModel { get; }

    public ProfilesPage(ProfilesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Initialize();
    }
}
