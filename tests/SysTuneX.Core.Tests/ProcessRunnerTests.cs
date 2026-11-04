using SysTuneX.Core.Services;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Which file actually runs when a service asks for a Windows tool by name.
///
/// SysTuneX runs as administrator from wherever it was downloaded to. A bare tool name used to be
/// searched for in that folder first, so a powercfg.exe left next to SysTuneX.exe was the one that
/// ran - elevated.
/// </summary>
public sealed class ProcessRunnerTests
{
    [Theory]
    [InlineData("powercfg.exe")]
    [InlineData("netsh.exe")]
    [InlineData("bcdedit.exe")]
    public void A_tool_named_without_a_path_runs_from_System32(string tool)
    {
        Assert.Equal(Path.Combine(Environment.SystemDirectory, tool), ProcessRunner.ResolveSystemTool(tool));
    }

    [Fact]
    public void A_full_path_is_left_as_it_was_given()
    {
        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

        Assert.Equal(powershell, ProcessRunner.ResolveSystemTool(powershell));
    }
}
