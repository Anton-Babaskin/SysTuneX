using System.Xml.Linq;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The UI falls back to the neutral English text in SysTuneX.Core when a translation is
/// missing, which is a good failure mode but a silent one. These tests make a missing Russian
/// string a build failure instead of something a user discovers.
/// </summary>
public sealed class LocalizationCoverageTests
{
    private static readonly Lazy<HashSet<string>> RussianKeys = new(() => LoadKeys("Strings.ru.resx"));
    private static readonly Lazy<HashSet<string>> NeutralKeys = new(() => LoadKeys("Strings.resx"));

    [Fact]
    public void Every_tweak_is_translated()
    {
        var missing = new List<string>();

        foreach (TweakDefinition tweak in TweakCatalog.All)
        {
            Require(missing, $"Tweak_{tweak.Id}_Name");
            Require(missing, $"Tweak_{tweak.Id}_Desc");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_profile_is_translated()
    {
        var missing = new List<string>();

        foreach (GameProfile profile in GameProfiles.BuiltIn)
        {
            Require(missing, $"Profile_{profile.Id}_Name");
            Require(missing, $"Profile_{profile.Id}_Desc");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_service_is_translated()
    {
        var missing = new List<string>();

        foreach (ServiceDefinition service in ServiceCatalog.All)
        {
            Require(missing, $"Service_{service.ServiceName}_Name");
            Require(missing, $"Service_{service.ServiceName}_Desc");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_cleanup_target_is_translated()
    {
        var missing = new List<string>();

        foreach (CleanupTarget target in CleanupCatalog.All)
        {
            Require(missing, $"Cleanup_{target.Id}_Name");
            Require(missing, $"Cleanup_{target.Id}_Desc");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_group_and_warning_key_used_by_the_catalog_exists()
    {
        var missing = new List<string>();

        foreach (string key in TweakCatalog.All.Select(t => t.GroupKey).Distinct())
        {
            if (!NeutralKeys.Value.Contains(key))
            {
                missing.Add($"{key} (neutral)");
            }

            Require(missing, key);
        }

        foreach (string key in TweakCatalog.All.Select(t => t.WarningKey).OfType<string>().Distinct())
        {
            if (!NeutralKeys.Value.Contains(key))
            {
                missing.Add($"{key} (neutral)");
            }

            Require(missing, key);
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_neutral_string_has_a_russian_counterpart()
    {
        string[] missing = NeutralKeys.Value.Except(RussianKeys.Value).Order().ToArray();

        Assert.Empty(missing);
    }

    private static void Require(List<string> missing, string key)
    {
        if (!RussianKeys.Value.Contains(key))
        {
            missing.Add(key);
        }
    }

    private static HashSet<string> LoadKeys(string fileName)
    {
        // Walk up from the test binaries to the repository root, so this works from both
        // `dotnet test` and an IDE run without hard-coding a path.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SysTuneX.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        string path = Path.Combine(directory.FullName, "src", "SysTuneX.App", "Resources", fileName);
        Assert.True(File.Exists(path), $"{fileName} was not found at {path}");

        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(e => e.Attribute("name")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }
}
