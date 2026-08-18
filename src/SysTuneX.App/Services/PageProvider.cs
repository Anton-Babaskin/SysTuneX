using Wpf.Ui.Abstractions;

namespace SysTuneX.App.Services;

/// <summary>
/// Lets NavigationView build pages through the container.
///
/// This is the piece the previous build was missing entirely: pages took their view model as a
/// constructor argument, but NavigationView was left to instantiate them itself, so every
/// navigation threw and the window stayed blank after the first page.
/// </summary>
public sealed class PageProvider : INavigationViewPageProvider
{
    private readonly IServiceProvider _services;

    public PageProvider(IServiceProvider services) => _services = services;

    public object? GetPage(Type pageType) => _services.GetService(pageType);
}
