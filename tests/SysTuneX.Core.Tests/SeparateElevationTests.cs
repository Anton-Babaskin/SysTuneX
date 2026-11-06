using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// SysTuneX always runs elevated, and since Administrator protection reached Windows 11 24H2 to
/// 26H2 an elevation can be a different account: a hidden, system-managed one with a profile of its
/// own. The same happens when a standard user types an administrator's password into the prompt.
/// HKCU in the process is then that account's hive, and every per-user tweak landed there - read
/// back as applied, invisible to the person at the screen. Store apps were listed and removed for
/// that account as well.
/// </summary>
public sealed class SeparateElevationTests
{
    private const string DesktopSid = "S-1-5-21-1004336348-1177238915-682003330-1001";

    private static readonly SeparateElevation Separate =
        new(@"PC\ADMIN_anton", @"PC\anton", DesktopSid);

    [Theory]
    [InlineData(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced")]
    public void Per_user_paths_go_to_the_desktop_accounts_hive(string keyPath)
    {
        (RegistryHive hive, string subKey) = RegistryService.ResolvePath(keyPath, DesktopSid);

        Assert.Equal(RegistryHive.Users, hive);
        Assert.Equal($@"{DesktopSid}\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", subKey);
    }

    [Fact]
    public void The_root_of_HKCU_is_the_root_of_the_desktop_accounts_hive()
    {
        Assert.Equal((RegistryHive.Users, DesktopSid), RegistryService.ResolvePath("HKCU", DesktopSid));
    }

    [Fact]
    public void Machine_wide_paths_are_not_redirected()
    {
        (RegistryHive hive, string subKey) = RegistryService.ResolvePath(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", DesktopSid);

        Assert.Equal(RegistryHive.LocalMachine, hive);
        Assert.Equal(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", subKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_a_separate_account_HKCU_stays_the_process_own(string? sid)
    {
        (RegistryHive hive, string subKey) = RegistryService.ResolvePath(@"HKCU\Control Panel\Mouse", sid);

        Assert.Equal(RegistryHive.CurrentUser, hive);
        Assert.Equal(@"Control Panel\Mouse", subKey);
    }

    [Fact]
    public async Task Store_apps_are_listed_and_removed_for_the_desktop_account()
    {
        var runner = new FakeProcessRunner();
        var packages = new AppPackageService(
            NullLogger<AppPackageService>.Instance,
            runner,
            new FakeDesktopSession { SeparateElevation = Separate });

        await packages.GetRemovableAppsAsync();
        await packages.RemoveAppAsync("Microsoft.BingNews");

        Assert.Collection(
            runner.Calls,
            list => Assert.Equal([DesktopSid], PowerShellArgumentTests.ArgumentsOf(list.Arguments)),
            remove => Assert.Equal(["Microsoft.BingNews", DesktopSid], PowerShellArgumentTests.ArgumentsOf(remove.Arguments)));
    }

    [Fact]
    public async Task Store_apps_are_the_process_own_when_it_is_the_desktop_account()
    {
        var runner = new FakeProcessRunner();
        var packages = new AppPackageService(NullLogger<AppPackageService>.Instance, runner, new FakeDesktopSession());

        await packages.GetRemovableAppsAsync();
        await packages.RemoveAppAsync("Microsoft.BingNews");

        Assert.Collection(
            runner.Calls,
            list => Assert.Empty(PowerShellArgumentTests.ArgumentsOf(list.Arguments)),
            remove => Assert.Equal(["Microsoft.BingNews"], PowerShellArgumentTests.ArgumentsOf(remove.Arguments)));
    }
}
