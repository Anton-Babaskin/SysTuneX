using Xunit;
using System.Reflection;
using System.Resources;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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
    /// <summary>Every page the navigation view can reach, in menu order.</summary>
    public static TheoryData<Type> PageTypes =>
    [
        typeof(DashboardPage),
        typeof(ProfilesPage),
        typeof(GamingPage),
        typeof(Windows11Page),
        typeof(ServicesPage),
        typeof(PrivacyPage),
        typeof(NetworkPage),
        typeof(CleanupPage),
        typeof(HistoryPage),
        typeof(SettingsPage),
    ];

    [Fact]
    public void Application_resources_load()
    {
        if (!host.IsSupported)
        {
            return;
        }

        Assert.Null(host.StartupFailure);
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

        host.OnUiThread(() => Assert.NotNull(App.Services.GetRequiredService<MainWindow>()));
    }

    [Theory]
    [MemberData(nameof(PageTypes))]
    public void Page_constructs(Type pageType)
    {
        if (!host.IsSupported)
        {
            return;
        }

        host.OnUiThread(() => Assert.NotNull(App.Services.GetRequiredService(pageType)));
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
