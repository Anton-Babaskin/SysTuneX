using System.Windows.Controls;
using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views;

public partial class PrivacyPage : Page
{
    public PrivacyViewModel ViewModel { get; }

    public PrivacyPage(PrivacyViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += (_, _) => ViewModel.Initialize();
    }
}
