using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Whether a restore point can be made decides whether the profiles page offers to make one
/// before it changes anything. Getting it wrong in the optimistic direction is the expensive
/// one: the option stays on, the user believes they have a safety net, and they find out
/// otherwise at the moment they need it.
///
/// The previous check ran a PowerShell command that counted restore points and then decided from
/// the exit code, ignoring the count entirely - and with -ErrorAction SilentlyContinue that exit
/// code is zero whether or not System Protection is on. It answered "available" every time,
/// including on a machine where protection was off, and no test could see it because the answer
/// came from spawning a shell.
/// </summary>
public sealed class RestorePointAvailabilityTests
{
    private const string SystemRestoreKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";
    private const string PolicyKey = @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";

    private static RestorePointService Service(FakeRegistryService registry, bool elevated = true) =>
        new(NullLogger<RestorePointService>.Instance, registry, new FakeEnvironment { IsElevated = elevated });

    [Fact]
    public async Task A_stock_machine_can_make_one()
    {
        // Nothing configured either way is the state most machines are in, and it works there.
        Assert.True(await Service(new FakeRegistryService()).IsAvailableAsync());
    }

    [Fact]
    public async Task Without_administrator_rights_it_cannot()
    {
        Assert.False(await Service(new FakeRegistryService(), elevated: false).IsAvailableAsync());
    }

    [Fact]
    public async Task System_restore_switched_off_for_the_machine_means_no()
    {
        var registry = new FakeRegistryService().Set(SystemRestoreKey, "DisableSR", 1);

        Assert.False(await Service(registry).IsAvailableAsync());
    }

    [Fact]
    public async Task Group_policy_switching_it_off_also_means_no()
    {
        var registry = new FakeRegistryService().Set(PolicyKey, "DisableSR", 1);

        Assert.False(await Service(registry).IsAvailableAsync());
    }

    [Fact]
    public async Task Protection_enabled_on_no_volume_means_no()
    {
        var registry = new FakeRegistryService().Set(SystemRestoreKey, "RPSessionInterval", 0);

        Assert.False(await Service(registry).IsAvailableAsync());
    }

    [Fact]
    public async Task Protection_enabled_means_yes()
    {
        var registry = new FakeRegistryService().Set(SystemRestoreKey, "RPSessionInterval", 1);

        Assert.True(await Service(registry).IsAvailableAsync());
    }

    [Fact]
    public async Task An_explicit_zero_in_DisableSR_is_not_a_refusal()
    {
        // Zero means "not disabled". Treating any present value as a refusal would turn the
        // feature off on machines that had explicitly turned it on.
        var registry = new FakeRegistryService()
            .Set(SystemRestoreKey, "DisableSR", 0)
            .Set(PolicyKey, "DisableSR", 0);

        Assert.True(await Service(registry).IsAvailableAsync());
    }
}
