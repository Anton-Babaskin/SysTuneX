using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Guards on the catalog data itself. These are the mistakes that are invisible in a code
/// review and only show up as a tweak that reports the wrong state on a stock machine.
/// </summary>
public sealed class TweakCatalogTests
{
    [Fact]
    public void Ids_are_unique_across_every_category()
    {
        // The previous build shipped two tweaks called service_kill_timeout and two called
        // sfio_priority, so a profile referring to either applied whichever was found first.
        string[] duplicates = TweakCatalog.All
            .GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_tweak_writes_something()
    {
        string[] empty = TweakCatalog.All
            .Where(t => t.Changes.Count == 0 && t.HandlerKey is null)
            .Select(t => t.Id)
            .ToArray();

        Assert.Empty(empty);
    }

    [Fact]
    public void Optimized_value_always_differs_from_the_windows_default()
    {
        // A change whose "optimised" value equals the stock value would read as already applied
        // on a clean install, which makes the tuning score lie.
        string[] noOps = TweakCatalog.All
            .SelectMany(t => t.Changes.Select(c => (Tweak: t, Change: c)))
            .Where(x => x.Change.WindowsDefaultValue is not null)
            .Where(x => Services.RegistryValueComparer.AreEqual(
                x.Change.OptimizedValue,
                x.Change.WindowsDefaultValue))
            .Select(x => $"{x.Tweak.Id}:{x.Change.ValueName}")
            .ToArray();

        Assert.Empty(noOps);
    }

    [Fact]
    public void Registry_paths_use_a_known_hive()
    {
        foreach (TweakDefinition tweak in TweakCatalog.All)
        {
            foreach (RegistryChange change in tweak.Changes)
            {
                // ParsePath throws on an unknown root, which is the assertion.
                (Microsoft.Win32.RegistryHive hive, string subKey) =
                    Services.RegistryService.ParsePath(change.KeyPath);

                Assert.True(Enum.IsDefined(hive));
                Assert.False(string.IsNullOrWhiteSpace(subKey), $"{tweak.Id} has an empty sub key");
            }
        }
    }

    [Fact]
    public void Value_names_are_never_empty()
    {
        string[] unnamed = TweakCatalog.All
            .SelectMany(t => t.Changes.Select(c => (t.Id, c.ValueName)))
            .Where(x => string.IsNullOrWhiteSpace(x.ValueName))
            .Select(x => x.Id)
            .ToArray();

        Assert.Empty(unnamed);
    }

    [Fact]
    public void Advanced_tweaks_carry_a_warning()
    {
        // Anything that needs a blocking confirmation must have something to say in it.
        string[] silent = TweakCatalog.All
            .Where(t => t.Risk == RiskLevel.Advanced)
            .Where(t => string.IsNullOrEmpty(t.WarningKey))
            .Select(t => t.Id)
            .ToArray();

        Assert.Empty(silent);
    }

    [Fact]
    public void Features_that_only_exist_on_windows_11_are_build_gated()
    {
        // Not every entry on the Windows 11 page is Windows 11 only - several are policies that
        // work on Windows 10 as well, and gating those would remove working features. What must
        // be gated is anything targeting a component Windows 10 does not have.
        string[] ungated = Windows11Tweaks.All
            .Where(t => t.GroupKey is "Group_Virtualization" or "Group_Ai")
            .Where(t => t.MinBuild < 22000)
            .Select(t => t.Id)
            .ToArray();

        Assert.Empty(ungated);
    }

    [Fact]
    public void Every_tweak_has_a_group_and_description()
    {
        foreach (TweakDefinition tweak in TweakCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(tweak.GroupKey), $"{tweak.Id} has no group");
            Assert.False(string.IsNullOrWhiteSpace(tweak.Name), $"{tweak.Id} has no name");
            Assert.True(tweak.Description.Length > 20, $"{tweak.Id} has a stub description");
        }
    }

    [Fact]
    public void Windows_10_sees_fewer_tweaks_and_none_of_the_windows_11_only_ones()
    {
        var windows10 = new WindowsVersionInfo { Major = 10, Minor = 0, Build = 19045 };
        var windows11 = new WindowsVersionInfo { Major = 10, Minor = 0, Build = 26100 };

        TweakDefinition[] onWindows10 = Windows11Tweaks.All.Where(t => t.AppliesTo(windows10)).ToArray();
        TweakDefinition[] onWindows11 = Windows11Tweaks.All.Where(t => t.AppliesTo(windows11)).ToArray();

        Assert.True(onWindows10.Length < onWindows11.Length);
        Assert.DoesNotContain(onWindows10, t => t.GroupKey is "Group_Virtualization" or "Group_Ai");
    }
}

public sealed class ServiceCatalogTests
{
    [Fact]
    public void Service_names_are_unique()
    {
        string[] duplicates = ServiceCatalog.All
            .GroupBy(s => s.ServiceName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Disabled_start_mode_is_never_automatic()
    {
        // "Turning off" a service by setting it to Automatic would be a no-op that still
        // recorded a backup entry.
        ServiceDefinition[] wrong = ServiceCatalog.All
            .Where(s => s.DisabledStartMode is ServiceStartMode.Automatic or ServiceStartMode.Unknown)
            .ToArray();

        Assert.Empty(wrong);
    }

    [Fact]
    public void Every_service_explains_itself()
    {
        foreach (ServiceDefinition service in ServiceCatalog.All)
        {
            Assert.True(service.Description.Length > 20, $"{service.ServiceName} has a stub description");
            Assert.False(string.IsNullOrWhiteSpace(service.DisplayName));
        }
    }
}

public sealed class GameProfileTests
{
    [Fact]
    public void Profiles_only_reference_tweaks_that_exist()
    {
        var unknown = new List<string>();

        foreach (GameProfile profile in GameProfiles.BuiltIn)
        {
            foreach (string tweakId in profile.TweakIds)
            {
                if (TweakCatalog.Find(tweakId) is null)
                {
                    unknown.Add($"{profile.Id} -> {tweakId}");
                }
            }
        }

        Assert.Empty(unknown);
    }

    [Fact]
    public void Profiles_only_reference_services_that_exist()
    {
        var unknown = new List<string>();

        foreach (GameProfile profile in GameProfiles.BuiltIn)
        {
            foreach (string serviceName in profile.ServiceNames)
            {
                if (ServiceCatalog.Find(serviceName) is null)
                {
                    unknown.Add($"{profile.Id} -> {serviceName}");
                }
            }
        }

        Assert.Empty(unknown);
    }

    [Fact]
    public void Profiles_never_contain_a_tweak_riskier_than_they_advertise()
    {
        var violations = new List<string>();

        foreach (GameProfile profile in GameProfiles.BuiltIn)
        {
            foreach (string tweakId in profile.TweakIds)
            {
                TweakDefinition? tweak = TweakCatalog.Find(tweakId);
                if (tweak is not null && tweak.Risk > profile.MaxRisk)
                {
                    violations.Add($"{profile.Id} declares {profile.MaxRisk} but includes {tweakId} ({tweak.Risk})");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Profile_ids_are_unique()
    {
        Assert.Equal(
            GameProfiles.BuiltIn.Count,
            GameProfiles.BuiltIn.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Tweak_ids_inside_a_profile_are_not_repeated()
    {
        foreach (GameProfile profile in GameProfiles.BuiltIn)
        {
            Assert.Equal(
                profile.TweakIds.Count,
                profile.TweakIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }
}

public sealed class CleanupCatalogTests
{
    [Fact]
    public void Target_ids_are_unique()
    {
        Assert.Equal(
            CleanupCatalog.All.Count,
            CleanupCatalog.All.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_target_has_at_least_one_path()
    {
        foreach (CleanupTarget target in CleanupCatalog.All)
        {
            Assert.NotEmpty(target.Paths);
        }
    }

    [Fact]
    public void Advanced_targets_are_not_selected_by_default()
    {
        // Deleting Prefetch should never happen because someone clicked Clean without reading.
        CleanupTarget[] risky = CleanupCatalog.All
            .Where(t => t.Risk == RiskLevel.Advanced && t.EnabledByDefault)
            .ToArray();

        Assert.Empty(risky);
    }
}
