using Xunit;
using System.Reflection;
using System.Resources;
using System.Windows;
using Wpf.Ui.Controls;
using Microsoft.Extensions.DependencyInjection;
using SysTuneX.App.Diagnostics;
using SysTuneX.App.Services;
using SysTuneX.App.Views;
using SysTuneX.App.Views.Pages;

namespace SysTuneX.App.Tests;

/// <summary>
/// The tests that answer "does it start?".
///
/// Everything here failed silently before: the build was green, the publish was green, and the
/// app died on the user's desktop with "Provide value on 'TypeConverterMarkupExtension' threw an
/// exception" because a .png referenced through a pack URI was never compiled into the assembly.
/// Nothing in a compile-only pipeline can catch that - the XAML is only parsed at run time.
/// </summary>
[Collection(WpfApplicationCollection.Name)]
public sealed class StartupSmokeTests(WpfApplicationFixture host)
{
    /// <summary>
    /// Every page in the app, found by reflection rather than listed by hand.
    ///
    /// This was a hand-written list, and a new page was added to the navigation without being
    /// added here - so the one page nobody had run yet was also the one page nothing tested. A
    /// list that has to be remembered is a list that will be forgotten, and the failure it lets
    /// through is the exact one this file exists to catch.
    /// </summary>
    public static TheoryData<Type> PageTypes => [.. DiscoverPages()];

    private static IReadOnlyList<Type> DiscoverPages() =>
        [.. typeof(App).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(SysTuneXPage).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Guards the discovery above. A query that quietly matched nothing would make every page
    /// test below pass without having run a single page.
    /// </summary>
    [Fact]
    public void Every_page_is_discovered()
    {
        IReadOnlyList<Type> pages = DiscoverPages();

        Assert.Contains(typeof(DashboardPage), pages);
        Assert.Contains(typeof(MonitorPage), pages);
        Assert.Contains(typeof(SettingsPage), pages);
        Assert.True(pages.Count >= 11, $"Only {pages.Count} pages were discovered.");
    }

    [Fact]
    public void Application_resources_load()
    {
        if (!host.IsSupported)
        {
            return;
        }

        Assert.True(
            host.StartupFailure is null,
            host.StartupFailure is null ? string.Empty : ExceptionReport.Describe(host.StartupFailure));

        Assert.NotNull(Application.Current);
    }

    /// <summary>
    /// Indexing a key forces WPF to realise the deferred content behind it. Styles, templates
    /// and converters are all lazy, so a dictionary that "loaded" may still hold a resource
    /// that throws the first time something asks for it - which, in the app, is at first paint.
    /// </summary>
    [Fact]
    public void Every_application_resource_materialises()
    {
        if (!host.IsSupported)
        {
            return;
        }

        var failures = new List<string>();

        host.OnUiThread(() =>
        {
            foreach (var dictionary in Flatten(Application.Current.Resources))
            {
                foreach (var key in dictionary.Keys.Cast<object>().ToList())
                {
                    try
                    {
                        _ = dictionary[key];
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{key}: {exception.Message}");
                    }
                }
            }
        });

        Assert.Empty(failures);

        // An empty dictionary would satisfy the loop above without proving anything, so pin one
        // key from each of the three dictionaries the app merges.
        host.OnUiThread(() =>
        {
            Assert.NotNull(Application.Current.TryFindResource("FilterToggle"));
            Assert.NotNull(Application.Current.TryFindResource("Card"));
            Assert.NotNull(Application.Current.TryFindResource("InverseBool"));
            Assert.NotNull(Application.Current.TryFindResource("TweakListTemplate"));
        });
    }

    /// <summary>
    /// Guards the exact defect that shipped: a pack URI is a compile-time string and a run-time
    /// lookup, so a resource that was never added to the project fails only when the XAML loads.
    /// </summary>
    [Theory]
    [InlineData("assets/systunex.png")]
    [InlineData("assets/systunex.ico")]
    public void Referenced_resource_is_embedded(string resourceName)
    {
        Assert.Contains(resourceName, EmbeddedResourceNames());
    }

    /// <summary>
    /// The whole window: BAML, title bar icon, banners, navigation items and their converters.
    /// This is the test that would have caught the crash.
    /// </summary>
    [Fact]
    public void MainWindow_constructs()
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            var window = App.Services.GetRequiredService<MainWindow>();
            Assert.NotNull(window);

            // Never shown - measuring is enough to apply the title bar, banners and navigation
            // templates, which is where a bad resource would surface.
            window.Measure(new Size(1240, 800));
            window.Arrange(new Rect(0, 0, 1240, 800));
            window.UpdateLayout();
        });
    }

    /// <summary>
    /// The window reaches a usable state whatever backdrop is saved.
    ///
    /// This is the test that was missing, and the bug it pins made the app unusable. Applying a
    /// backdrop other than the one the XAML declares makes WPF-UI rebuild the window chrome, and
    /// replacing a WindowChrome that already carries an inheritance context throws from inside WPF.
    /// That assignment sat at the *top* of the window's Loaded handler, so the throw took the rest
    /// of it with it - including the navigation on the line below. Anyone who picked Acrylic or
    /// None in settings got an empty window on every launch afterwards, plus a second crash from
    /// the title bar's close button, which had never finished initialising either.
    ///
    /// Nothing caught it. The existing MainWindow test uses default settings, which say Mica -
    /// the same value the XAML declares - so no change ever fired; and a window that is never
    /// shown never raises Loaded, so the handler itself ran in no test at all.
    ///
    /// The assertion is deliberately about navigation, not about the backdrop: WPF-UI may well
    /// still refuse the change, and that is its business. What matters is that a decorative
    /// setting cannot stop the window showing its content.
    /// </summary>
    [Theory]
    [InlineData(WindowBackdropType.Mica)]
    [InlineData(WindowBackdropType.Acrylic)]
    [InlineData(WindowBackdropType.Tabbed)]
    [InlineData(WindowBackdropType.None)]
    [InlineData(WindowBackdropType.Auto)]
    public void The_window_navigates_whatever_backdrop_is_saved(WindowBackdropType backdrop)
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            App.Services.GetRequiredService<IAppSettingsService>().Current.Backdrop = backdrop;

            var window = App.Services.GetRequiredService<MainWindow>();
            window.Measure(new Size(1240, 800));
            window.Arrange(new Rect(0, 0, 1240, 800));
            window.UpdateLayout();

            Exception? failure = Record.Exception(window.ApplyStartupState);

            Assert.True(
                failure is null,
                $"Startup threw with {backdrop}: {failure?.GetType().Name}: {failure?.Message}");
        });
    }

    /// <summary>
    /// Constructing a page parses its BAML, but the shared control templates it pulls out of
    /// PageParts.xaml are only instantiated when layout runs - so a resource that is missing
    /// inside a template survives construction and dies at first paint. Running a real measure
    /// and arrange pass forces the templates to apply. No display is involved; WPF lays out
    /// perfectly well off-screen.
    /// </summary>
    [Theory]
    [MemberData(nameof(PageTypes))]
    public void Page_constructs_and_lays_out(Type pageType)
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() =>
        {
            var page = App.Services.GetRequiredService(pageType);
            Assert.NotNull(page);

            var element = Assert.IsAssignableFrom<FrameworkElement>(page);
            element.Measure(new Size(1240, 800));
            element.Arrange(new Rect(0, 0, 1240, 800));
            element.UpdateLayout();
        });
    }

    /// <summary>Reads the names WPF compiled into the app assembly's generated resource stream.</summary>
    private static IReadOnlyCollection<string> EmbeddedResourceNames()
    {
        var assembly = typeof(App).Assembly;
        var name = $"{assembly.GetName().Name}.g.resources";

        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is missing from {assembly.GetName().Name}.");
        using var reader = new ResourceReader(stream);

        // Read keys only. Touching Entry would deserialise each value, and there is no reason
        // to materialise every BAML stream just to find out what the assembly contains.
        var names = new List<string>();
        var enumerator = reader.GetEnumerator();
        while (enumerator.MoveNext())
        {
            names.Add((string)enumerator.Key);
        }

        return names;
    }

    private static IEnumerable<ResourceDictionary> Flatten(ResourceDictionary dictionary)
    {
        yield return dictionary;

        foreach (var merged in dictionary.MergedDictionaries)
        {
            foreach (var nested in Flatten(merged))
            {
                yield return nested;
            }
        }
    }
}
