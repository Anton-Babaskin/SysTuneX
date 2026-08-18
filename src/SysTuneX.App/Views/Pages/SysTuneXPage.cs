using System.Windows.Controls;
using SysTuneX.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace SysTuneX.App.Views.Pages;

/// <summary>
/// Base for every page in the app. It is the XAML root of each page, which keeps the
/// code-behind down to a constructor and lets navigation drive the view model lifecycle:
/// entering a page starts its work, leaving it cancels whatever is still running.
/// </summary>
public abstract class SysTuneXPage : Page, INavigationAware
{
    private PageViewModel? _viewModel;

    protected SysTuneXPage()
    {
        // Pages are cached by NavigationView, so scroll position and state survive navigation.
        ScrollViewer.SetCanContentScroll(this, false);
    }

    /// <summary>Called from the derived constructor before InitializeComponent.</summary>
    protected void Bind(PageViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public Task OnNavigatedToAsync() => _viewModel?.EnterAsync() ?? Task.CompletedTask;

    public Task OnNavigatedFromAsync() => _viewModel?.LeaveAsync() ?? Task.CompletedTask;
}
