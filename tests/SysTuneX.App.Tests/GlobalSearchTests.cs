using System.IO;
using System.ComponentModel;
using System.Globalization;
using SysTuneX.App.Localization;
using SysTuneX.App.Services;
using SysTuneX.App.Views.Pages;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;
using Xunit;

namespace SysTuneX.App.Tests;

/// <summary>
/// A search over a hundred entries is only useful if the right thing is near the top. These pin
/// the ranking rather than the fact that something came back.
/// </summary>
public sealed class GlobalSearchTests
{
    [Fact]
    public void A_single_letter_returns_nothing()
    {
        // One letter matches most of the catalog. That is not a result list, it is noise.
        Assert.Empty(Search().Search("g"));
    }

    [Fact]
    public void An_empty_query_returns_nothing()
    {
        Assert.Empty(Search().Search(string.Empty));
        Assert.Empty(Search().Search("   "));
    }

    [Fact]
    public void A_name_match_outranks_a_description_match()
    {
        TweakDefinition tweak = TweakCatalog.All.First(t => t.Id == "game_bar_disable");

        // The whole name, so this is a prefix match and must come first.
        IReadOnlyList<SearchHit> hits = Search().Search(tweak.Name);

        Assert.NotEmpty(hits);
        Assert.Equal(tweak.Name, hits[0].Title);
    }

    /// <summary>
    /// Someone who knows the value is called HwSchMode should not have to guess what the tweak
    /// was named in their language.
    /// </summary>
    [Fact]
    public void The_catalog_identifier_is_searchable_too()
    {
        TweakDefinition tweak = TweakCatalog.All.First(t => t.Id == "game_bar_disable");

        IReadOnlyList<SearchHit> hits = Search().Search("game_bar_disable");

        Assert.Contains(hits, hit => hit.Title == tweak.Name);
    }

    [Fact]
    public void Results_carry_the_page_that_owns_them()
    {
        TweakDefinition tweak = TweakCatalog.All.First(t => t.Id == "game_bar_disable");

        SearchHit hit = Assert.Single(Search().Search(tweak.Name), h => h.Title == tweak.Name);

        Assert.Equal(typeof(GamingPage), hit.PageType);

        // The filter is what the destination page puts in its own search box, so the item is
        // the one row on screen rather than one of thirty.
        Assert.Equal(hit.Title, hit.Filter);
    }

    [Fact]
    public void Services_are_searchable_and_point_at_the_services_page()
    {
        ServiceDefinition service = ServiceCatalog.All.First(s => s.MinBuild == 0);

        IReadOnlyList<SearchHit> hits = Search().Search(service.DisplayName);

        Assert.Contains(hits, hit => hit.Title == service.DisplayName && hit.PageType == typeof(ServicesPage));
    }

    [Fact]
    public void Cleanup_targets_are_searchable()
    {
        CleanupTarget target = CleanupCatalog.All[0];

        Assert.Contains(
            Search().Search(target.Name),
            hit => hit.Title == target.Name && hit.PageType == typeof(CleanupPage));
    }

    [Fact]
    public void The_result_count_is_capped()
    {
        // "dis" appears in a good many names and descriptions - enough to overflow the cap.
        Assert.True(Search().Search("dis", limit: 5).Count <= 5);
        Assert.NotEmpty(Search().Search("dis", limit: 5));
    }

    [Fact]
    public void Nothing_matching_returns_nothing()
    {
        Assert.Empty(Search().Search("zzzznotathing"));
    }

    /// <summary>
    /// A service Windows 10 does not have is not on the services page either, so finding it
    /// would send the user to a page where it is not listed.
    /// </summary>
    [Fact]
    public void A_service_the_running_build_does_not_have_is_not_findable()
    {
        ServiceDefinition gated = ServiceCatalog.All.First(s => s.MinBuild >= 22000);

        var windows10 = new StubEnvironment
        {
            Windows = new WindowsVersionInfo { Major = 10, Minor = 0, Build = 19045, ProductName = "Windows 10 Pro" },
        };

        IReadOnlyList<SearchHit> hits = Search(windows10).Search(gated.ServiceName, limit: 50);

        Assert.DoesNotContain(hits, hit => hit.Title == gated.DisplayName);
    }

    private static GlobalSearch Search(StubEnvironment? environment = null)
    {
        var localization = new StubLocalization();
        StubEnvironment env = environment ?? new StubEnvironment();

        return new GlobalSearch(new StubTweakEngine(env), env, localization, new CatalogText(localization));
    }

    /// <summary>Returns the neutral text, which is what the catalog carries.</summary>
    private sealed class StubLocalization : ILocalizationService
    {
        public string this[string key] => key;

        public string Get(string key, string? fallback = null) => fallback ?? key;

        public string Format(string key, params object?[] arguments) => key;

        public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

        public string SelectedLanguageCode => string.Empty;

        public IReadOnlyList<LanguageOption> AvailableLanguages => [];

        public void SetLanguage(string languageCode)
        {
        }

        public event EventHandler? LanguageChanged { add { } remove { } }

        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    private sealed class StubEnvironment : IEnvironmentService
    {
        public bool IsElevated => true;

        public WindowsVersionInfo Windows { get; set; } = new()
        {
            Major = 10,
            Minor = 0,
            Build = 26100,
            ProductName = "Windows 11 Pro",
        };

        public string DataDirectory => Path.GetTempPath();

        public OperationResult RestartElevated() => OperationResult.Ok();

        public Task<OperationResult> RestartExplorerAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());
    }

    /// <summary>The real catalog, gated the way the real engine gates it.</summary>
    private sealed class StubTweakEngine(IEnvironmentService environment) : ITweakEngine
    {
        public IReadOnlyList<TweakDefinition> GetSupportedTweaks(TweakCategory? category = null) =>
            [.. TweakCatalog.All
                .Where(t => t.AppliesTo(environment.Windows))
                .Where(t => category is null || t.Category == category)];

        public TweakDefinition? Find(string tweakId) =>
            TweakCatalog.All.FirstOrDefault(t => t.Id == tweakId);

        public TweakStatus GetStatus(TweakDefinition tweak) => TweakStatus.Unknown;

        public Task<OperationResult> ApplyAsync(TweakDefinition tweak, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> RevertAsync(TweakDefinition tweak, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<BatchResult> ApplyManyAsync(IEnumerable<TweakDefinition> tweaks, IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(BatchResult.Empty);

        public Task<BatchResult> RevertManyAsync(IEnumerable<TweakDefinition> tweaks, IProgress<BatchProgress>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(BatchResult.Empty);
    }
}
