using System.Windows.Controls;
using SysTuneX.App.ViewModels;

namespace SysTuneX.App.Views;

public partial class ProfilePreviewDialog : UserControl
{
    public ProfilePreviewDialog(ProfilePreviewViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
