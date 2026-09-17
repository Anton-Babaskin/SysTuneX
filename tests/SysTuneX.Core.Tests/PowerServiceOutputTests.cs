using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// What powercfg prints, and what SysTuneX makes of it.
///
/// None of this could be tested until the process runner became something that could be handed in.
/// The scheme regex has a comment explaining that powercfg's labels are translated and that only
/// the GUID and the parenthesised name can be relied on - written from reasoning, never once run
/// against a Russian machine's output. So did the power setting reader, and that one turned out to
/// be wrong.
/// </summary>
public sealed class PowerServiceOutputTests
{
    private const string EnglishList = """
        Existing Power Schemes (* Active)
        -----------------------------------
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
        Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
        Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)
        """;

    private const string RussianList = """
        Существующие схемы управления питанием (* активная)
        -----------------------------------
        GUID схемы питания: 381b4222-f694-41f0-9685-ff5bb260df2e  (Сбалансированная)
        GUID схемы питания: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (Высокая производительность) *
        GUID схемы питания: a1841308-3541-4fab-bc81-f71556f20b4a  (Экономия энергии)
        """;

    private static PowerService Service(FakeProcessRunner processes) =>
        new(NullLogger<PowerService>.Instance, new FakeBackupService(), processes, new FakeEnvironment());

    [Fact]
    public async Task Every_scheme_is_read_from_the_english_list()
    {
        IReadOnlyList<PowerScheme> schemes =
            await Service(new FakeProcessRunner().Printing("/list", EnglishList)).GetSchemesAsync();

        Assert.Equal(3, schemes.Count);
        Assert.Equal("Balanced", schemes[0].Name);
        Assert.Equal(PowerScheme.Balanced, schemes[0].Guid);
    }

    /// <summary>
    /// The labels around the GUID are translated, which is why the regex reads the GUID and the
    /// parenthesised name and ignores everything else. This is the first time that claim has been
    /// checked against the output it is a claim about.
    /// </summary>
    [Fact]
    public async Task Every_scheme_is_read_from_the_russian_list_too()
    {
        IReadOnlyList<PowerScheme> schemes =
            await Service(new FakeProcessRunner().Printing("/list", RussianList)).GetSchemesAsync();

        Assert.Equal(3, schemes.Count);
        Assert.Equal("Сбалансированная", schemes[0].Name);
        Assert.Equal(PowerScheme.HighPerformance, schemes[1].Guid);
    }

    /// <summary>The asterisk marks the active one, and it is on a different row in each language.</summary>
    [Theory]
    [InlineData(EnglishList, 0)]
    [InlineData(RussianList, 1)]
    public async Task The_active_scheme_is_the_one_with_the_asterisk(string output, int expectedIndex)
    {
        IReadOnlyList<PowerScheme> schemes =
            await Service(new FakeProcessRunner().Printing("/list", output)).GetSchemesAsync();

        Assert.True(schemes[expectedIndex].IsActive, "The starred scheme was not the one reported active.");
        Assert.Single(schemes, scheme => scheme.IsActive);
    }

    /// <summary>
    /// A header line with no GUID in it must not become a scheme. The list starts with two of them
    /// and a row of dashes.
    /// </summary>
    [Fact]
    public async Task Header_lines_are_not_mistaken_for_schemes()
    {
        IReadOnlyList<PowerScheme> schemes =
            await Service(new FakeProcessRunner().Printing("/list", EnglishList)).GetSchemesAsync();

        Assert.All(schemes, scheme => Assert.NotEqual(Guid.Empty, scheme.Guid));
    }

    [Fact]
    public async Task A_powercfg_that_will_not_run_yields_no_schemes_rather_than_throwing()
    {
        IReadOnlyList<PowerScheme> schemes =
            await Service(new FakeProcessRunner().Failing("/list")).GetSchemesAsync();

        Assert.Empty(schemes);
    }

    [Fact]
    public async Task The_active_scheme_is_read_on_its_own()
    {
        var processes = new FakeProcessRunner().Printing(
            "/getactivescheme",
            "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)");

        PowerScheme? active = await Service(processes).GetActiveSchemeAsync();

        Assert.NotNull(active);
        Assert.Equal(PowerScheme.HighPerformance, active.Guid);
        Assert.Equal("High performance", active.Name);
    }

    [Fact]
    public async Task An_unreadable_active_scheme_is_null_rather_than_a_guess()
    {
        Assert.Null(await Service(new FakeProcessRunner().Failing("/getactivescheme")).GetActiveSchemeAsync());
    }

    /// <summary>
    /// A setting has to be written on mains *and* on battery, or a laptop reverts the moment it is
    /// unplugged - and the scheme has to be re-activated afterwards or the value is stored without
    /// being in force. All three calls, in that order.
    /// </summary>
    [Fact]
    public async Task Writing_a_setting_covers_mains_battery_and_a_reactivation()
    {
        var processes = new FakeProcessRunner();

        OperationResult result = await Service(processes)
            .SetSchemeSettingAsync("sub", "setting", 7, CoreMessages.PowerSettingRejected);

        Assert.True(result.Success);
        Assert.Equal(
            ["/setacvalueindex SCHEME_CURRENT sub setting 7",
             "/setdcvalueindex SCHEME_CURRENT sub setting 7",
             "/setactive SCHEME_CURRENT"],
            processes.ArgumentsMatching("powercfg"));
    }

    /// <summary>
    /// If powercfg refuses the write, the scheme must not be re-activated as though it had worked.
    /// Reporting a failure and then doing the confirming step anyway is how a user ends up trusting
    /// the wrong one of the two.
    /// </summary>
    [Fact]
    public async Task A_refused_write_is_reported_and_not_followed_by_a_reactivation()
    {
        var processes = new FakeProcessRunner().Failing("setacvalueindex", "Invalid Parameters");

        OperationResult result = await Service(processes)
            .SetSchemeSettingAsync("sub", "setting", 7, CoreMessages.PowerSettingRejected);

        Assert.False(result.Success);
        Assert.DoesNotContain("/setactive SCHEME_CURRENT", processes.ArgumentsMatching("powercfg"));
    }

    [Fact]
    public async Task A_setting_the_machine_does_not_expose_reads_as_nothing()
    {
        var processes = new FakeProcessRunner().Failing("/q");

        Assert.Null(await Service(processes).GetSchemeSettingAsync("sub", "setting"));
    }

    [Fact]
    public async Task A_setting_that_answers_is_read_back()
    {
        var processes = new FakeProcessRunner().Printing(
            "/q",
            "  Current AC Power Setting Index: 0x00000064\n  Current DC Power Setting Index: 0x00000005");

        Assert.Equal(100, await Service(processes).GetSchemeSettingAsync("sub", "setting"));
    }
}
