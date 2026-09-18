using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// powercfg prints in the system's language, and the line carrying the value is found by looking
/// for a word in it. That had never been run against either language it claims to handle, because
/// reaching it needed powercfg on a Windows machine. These are the outputs it has to read.
/// </summary>
public sealed class PowerSettingIndexTests
{
    private const string English = """
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
          Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
            Power Setting GUID: 0cc5b647-c1df-4637-891a-dec35c318583  (Processor performance core parking min cores)
              Minimum Possible Setting: 0x00000000
              Maximum Possible Setting: 0x00000064
              Possible Settings increment: 0x00000001
              Possible Settings units: %
            Current AC Power Setting Index: 0x00000064
            Current DC Power Setting Index: 0x00000005
        """;

    private const string Russian = """
        GUID схемы питания: 381b4222-f694-41f0-9685-ff5bb260df2e  (Сбалансированная)
          GUID подгруппы: 54533251-82be-4824-96c1-47b60b740d00  (Управление питанием процессора)
            GUID параметра питания: 0cc5b647-c1df-4637-891a-dec35c318583  (Минимальное число ядер)
              Минимально возможное значение: 0x00000000
              Максимально возможное значение: 0x00000064
            Текущий индекс параметра питания от сети: 0x00000064
            Текущий индекс параметра питания от батареи: 0x00000005
        """;

    [Theory]
    [InlineData(English)]
    [InlineData(Russian)]
    public void The_mains_value_is_read_in_either_language(string output)
    {
        Assert.Equal(100, PowerSettingIndex.Parse(output));
    }

    [Theory]
    [InlineData(English)]
    [InlineData(Russian)]
    public void The_battery_value_is_the_second_one(string output)
    {
        Assert.Equal(5, PowerSettingIndex.Parse(output, PowerSettingIndex.Source.DC));
    }

    /// <summary>
    /// The lines above the value also carry hex numbers - the minimum, the maximum, the increment.
    /// Taking the first hex in the output rather than the first on an index line would read the
    /// setting's lower bound and call it the current value.
    /// </summary>
    [Fact]
    public void The_possible_range_printed_above_is_not_mistaken_for_the_value()
    {
        Assert.NotEqual(0, PowerSettingIndex.Parse(English));
    }

    /// <summary>
    /// A hidden setting is not a setting worth zero. Several of these treat zero as "off", so a
    /// guess here would report a machine as tuned when the setting does not exist on it.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Invalid Parameters -- try \"/?\" for help")]
    public void Output_with_no_value_in_it_reads_as_nothing_rather_than_zero(string? output)
    {
        Assert.Null(PowerSettingIndex.Parse(output));
    }

    /// <summary>
    /// Zero is a real answer for most of these settings - ASPM off, throttling off - so it has to
    /// survive being read. This is the mirror of the test above.
    /// </summary>
    [Fact]
    public void A_genuine_zero_is_read_as_zero()
    {
        const string off = """
            Power Setting GUID: ee12f906-d277-404b-b6da-e5fa1a576df5  (Link State Power Management)
              Current AC Power Setting Index: 0x00000000
              Current DC Power Setting Index: 0x00000002
            """;

        Assert.Equal(0, PowerSettingIndex.Parse(off));
        Assert.Equal(2, PowerSettingIndex.Parse(off, PowerSettingIndex.Source.DC));
    }

    /// <summary>
    /// A machine plugged in with no battery prints only the AC line. Asking for the battery value
    /// must say "there isn't one" rather than handing back the mains value by accident.
    /// </summary>
    [Fact]
    public void A_missing_battery_line_is_not_answered_with_the_mains_value()
    {
        const string acOnly = "  Current AC Power Setting Index: 0x0000000a";

        Assert.Equal(10, PowerSettingIndex.Parse(acOnly));
        Assert.Null(PowerSettingIndex.Parse(acOnly, PowerSettingIndex.Source.DC));
    }

    /// <summary>Windows writes CRLF; splitting on newline alone must not leave a stray return.</summary>
    [Fact]
    public void Windows_line_endings_are_handled()
    {
        Assert.Equal(100, PowerSettingIndex.Parse(English.ReplaceLineEndings("\r\n")));
    }
}
