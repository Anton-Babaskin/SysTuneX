using SysTuneX.Core.Models;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The failure worth guarding against: a query that matches nothing reports every task as already
/// disabled, and the tweak then claims to have done work it never did. Everything here is about
/// telling "switched off" apart from "could not tell".
/// </summary>
public sealed class ScheduledTaskQueryTests
{
    [Fact]
    public void A_normal_answer_is_read()
    {
        IReadOnlyList<ScheduledTaskInfo> tasks = ScheduledTaskQuery.Parse("""
            \Microsoft\Windows\Application Experience\ProgramDataUpdater|Ready
            \Microsoft\Windows\Autochk\Proxy|Disabled
            \Microsoft\Windows\Customer Experience Improvement Program\Consolidator|Running
            """);

        Assert.Equal(3, tasks.Count);
        Assert.Equal(ScheduledTaskState.Ready, tasks[0].State);
        Assert.Equal(ScheduledTaskState.Disabled, tasks[1].State);
        Assert.Equal(ScheduledTaskState.Running, tasks[2].State);
    }

    /// <summary>
    /// A state this build has never heard of is Unknown, not Disabled. Guessing "disabled" would
    /// make the tweak report success without touching anything - which is precisely what using
    /// schtasks would have done on a Russian install, where the state prints as "Готово".
    /// </summary>
    [Theory]
    [InlineData(@"\Some\Task|Queued")]
    [InlineData(@"\Some\Task|Готово")]
    public void An_unrecognised_state_is_unknown_rather_than_disabled(string line)
    {
        Assert.Equal(ScheduledTaskState.Unknown, Assert.Single(ScheduledTaskQuery.Parse(line)).State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Get-ScheduledTask : No MSFT_ScheduledTask objects found")]
    [InlineData(@"\Some\Task|")]
    [InlineData("|Ready")]
    public void Output_with_nothing_usable_in_it_yields_no_tasks(string? output)
    {
        Assert.Empty(ScheduledTaskQuery.Parse(output));
    }

    /// <summary>
    /// Get-ScheduledTask gives the folder with a trailing backslash and the name separately, so a
    /// joined path has a double separator in it. Comparing that against the catalogue's spelling is
    /// how a task that is right there looks absent.
    /// </summary>
    [Theory]
    [InlineData(@"\Microsoft\Windows\Autochk\\Proxy")]
    [InlineData(@"Microsoft\Windows\Autochk\Proxy")]
    [InlineData(@"\Microsoft\Windows\Autochk\Proxy\")]
    [InlineData(@"  \Microsoft\Windows\Autochk\Proxy  ")]
    public void Paths_are_normalised_to_one_spelling(string path)
    {
        Assert.Equal(@"\Microsoft\Windows\Autochk\Proxy", ScheduledTaskQuery.Normalize(path));
    }

    [Fact]
    public void A_parsed_path_is_normalised_too()
    {
        Assert.Equal(
            @"\Microsoft\Windows\Autochk\Proxy",
            Assert.Single(ScheduledTaskQuery.Parse(@"\Microsoft\Windows\Autochk\\Proxy|Disabled")).Path);
    }

    /// <summary>Windows writes CRLF; a stray carriage return would land in the state word.</summary>
    [Fact]
    public void Windows_line_endings_do_not_break_the_state()
    {
        IReadOnlyList<ScheduledTaskInfo> tasks =
            ScheduledTaskQuery.Parse("\\A\\B|Ready\r\n\\C\\D|Disabled\r\n");

        Assert.Equal(2, tasks.Count);
        Assert.Equal(ScheduledTaskState.Ready, tasks[0].State);
        Assert.Equal(ScheduledTaskState.Disabled, tasks[1].State);
    }

    /// <summary>
    /// Task names can be almost anything, so the split has to be from the right. Splitting on the
    /// first separator would cut a name in half and lose the state.
    /// </summary>
    [Fact]
    public void A_separator_inside_a_task_name_does_not_confuse_the_split()
    {
        ScheduledTaskInfo task = Assert.Single(ScheduledTaskQuery.Parse(@"\Vendor\Odd|Name|Ready"));

        Assert.Equal(@"\Vendor\Odd|Name", task.Path);
        Assert.Equal(ScheduledTaskState.Ready, task.State);
    }
}
