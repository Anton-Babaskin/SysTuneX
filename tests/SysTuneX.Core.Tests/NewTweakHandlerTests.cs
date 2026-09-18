using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// A scheduled-task service that answers from a dictionary, so the handler's decisions can be
/// tested without PowerShell or a Windows machine.
/// </summary>
public sealed class FakeScheduledTaskService : IScheduledTaskService
{
    private readonly Dictionary<string, ScheduledTaskState> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tasks this run was asked to change, and what to.</summary>
    public List<(string Path, bool Enabled)> Changes { get; } = [];

    /// <summary>Makes the query itself fail, as it does without administrator rights.</summary>
    public bool QueryFails { get; set; }

    public FakeScheduledTaskService With(string path, ScheduledTaskState state)
    {
        _states[ScheduledTaskQuery.Normalize(path)] = state;
        return this;
    }

    public Task<IReadOnlyList<ScheduledTaskInfo>> GetStateAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        if (QueryFails)
        {
            return Task.FromResult<IReadOnlyList<ScheduledTaskInfo>>([]);
        }

        // Tasks absent from the dictionary stand for tasks this build of Windows does not have,
        // which the real service omits rather than reporting.
        IReadOnlyList<ScheduledTaskInfo> found =
        [
            .. paths
                .Select(ScheduledTaskQuery.Normalize)
                .Where(_states.ContainsKey)
                .Select(path => new ScheduledTaskInfo(path, _states[path])),
        ];

        return Task.FromResult(found);
    }

    public Task<OperationResult> SetEnabledAsync(
        IReadOnlyList<string> paths,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        foreach (string path in paths)
        {
            string normalized = ScheduledTaskQuery.Normalize(path);
            Changes.Add((normalized, enabled));
            _states[normalized] = enabled ? ScheduledTaskState.Ready : ScheduledTaskState.Disabled;
        }

        return Task.FromResult(OperationResult.Ok());
    }
}

/// <summary>
/// PCI Express link state power management. The question worth testing is not whether powercfg
/// runs - it is whether the handler can tell "switched off" from "this machine has no such
/// setting", because those look identical if you read a missing answer as zero.
/// </summary>
public sealed class PcieAspmTweakHandlerTests
{
    private const string Key =
        "501a4d13-42af-4429-9fd1-a8218c268e20/ee12f906-d277-404b-b6da-e5fa1a576df5";

    private static (PcieAspmTweakHandler Handler, FakePowerService Power) Build()
    {
        var power = new FakePowerService();
        return (new PcieAspmTweakHandler(power, new FakeBackupService()), power);
    }

    [Fact]
    public async Task A_machine_that_does_not_expose_the_setting_reports_unknown()
    {
        (PcieAspmTweakHandler handler, _) = Build();

        Assert.Equal(TweakStatus.Unknown, await handler.GetStatusAsync());
    }

    [Fact]
    public async Task Applying_it_on_a_machine_without_the_setting_fails_rather_than_claiming_success()
    {
        (PcieAspmTweakHandler handler, FakePowerService power) = Build();

        OperationResult result = await handler.ApplyAsync();

        Assert.False(result.Success);
        Assert.False(power.SchemeSettings.ContainsKey(Key));
    }

    [Theory]
    [InlineData(0, TweakStatus.Applied)]
    [InlineData(1, TweakStatus.NotApplied)]
    [InlineData(2, TweakStatus.NotApplied)]
    public async Task The_status_follows_the_stored_index(int current, TweakStatus expected)
    {
        (PcieAspmTweakHandler handler, FakePowerService power) = Build();
        power.SchemeSettings[Key] = current;

        Assert.Equal(expected, await handler.GetStatusAsync());
    }

    [Fact]
    public async Task Applying_it_writes_zero()
    {
        (PcieAspmTweakHandler handler, FakePowerService power) = Build();
        power.SchemeSettings[Key] = 2;

        Assert.True((await handler.ApplyAsync()).Success);
        Assert.Equal(0, power.SchemeSettings[Key]);
    }

    [Fact]
    public async Task Applying_it_twice_is_not_a_second_change()
    {
        (PcieAspmTweakHandler handler, FakePowerService power) = Build();
        power.SchemeSettings[Key] = 0;

        OperationResult result = await handler.ApplyAsync();

        Assert.True(result.Success);
        Assert.False(result.Changed);
    }

    /// <summary>
    /// The value the machine actually had, not the one a clean install would have. OEM schemes
    /// differ here, and putting back a guess leaves a PC in a state it was never in.
    /// </summary>
    [Fact]
    public async Task Reverting_puts_back_what_was_recorded()
    {
        (PcieAspmTweakHandler handler, FakePowerService power) = Build();
        power.SchemeSettings[Key] = 1;

        await handler.ApplyAsync();
        Assert.Equal(0, power.SchemeSettings[Key]);

        await handler.RevertAsync();
        Assert.Equal(1, power.SchemeSettings[Key]);
    }

    /// <summary>
    /// Nothing recorded means the change was made by something other than this app, or the journal
    /// was cleared. Two - maximum power savings - is what Windows ships on every scheme but High
    /// Performance, so it is the one defensible guess.
    /// </summary>
    [Fact]
    public async Task Reverting_with_no_record_restores_the_windows_default()
    {
        (PcieAspmTweakHandler handler, FakePowerService power) = Build();
        power.SchemeSettings[Key] = 0;

        await handler.RevertAsync();

        Assert.Equal(2, power.SchemeSettings[Key]);
    }
}

/// <summary>
/// The telemetry scheduled tasks. The rule that matters is the same one the service tweaks follow:
/// put back what we switched off, and nothing else.
/// </summary>
public sealed class TelemetryTaskTweakHandlerTests
{
    private const string Appraiser =
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser";

    private const string ProgramData =
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater";

    private const string UsbCeip =
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip";

    private static TelemetryTaskTweakHandler Build(FakeScheduledTaskService tasks) =>
        new(tasks, new FakeBackupService());

    /// <summary>
    /// A query that answered nothing reports Unknown, not NotApplied. Reporting NotApplied would
    /// offer a switch that cannot do anything, and - worse - the apply that followed would report
    /// success having changed nothing.
    /// </summary>
    [Fact]
    public async Task A_query_that_answers_nothing_reports_unknown()
    {
        Assert.Equal(
            TweakStatus.Unknown,
            await Build(new FakeScheduledTaskService { QueryFails = true }).GetStatusAsync());
    }

    [Fact]
    public async Task All_off_is_applied_and_all_on_is_not()
    {
        var allOn = new FakeScheduledTaskService();
        var allOff = new FakeScheduledTaskService();

        foreach (string path in TelemetryTaskTweakHandler.Tasks)
        {
            allOn.With(path, ScheduledTaskState.Ready);
            allOff.With(path, ScheduledTaskState.Disabled);
        }

        Assert.Equal(TweakStatus.NotApplied, await Build(allOn).GetStatusAsync());
        Assert.Equal(TweakStatus.Applied, await Build(allOff).GetStatusAsync());
    }

    [Fact]
    public async Task Some_off_and_some_on_is_partial()
    {
        var mixed = new FakeScheduledTaskService()
            .With(Appraiser, ScheduledTaskState.Disabled)
            .With(ProgramData, ScheduledTaskState.Ready);

        Assert.Equal(TweakStatus.Partial, await Build(mixed).GetStatusAsync());
    }

    /// <summary>
    /// Windows editions differ, and a task that is not on this build is not a failure. Only the
    /// ones that exist count towards the verdict.
    /// </summary>
    [Fact]
    public async Task Tasks_this_windows_does_not_have_do_not_count_against_the_verdict()
    {
        var partial = new FakeScheduledTaskService().With(Appraiser, ScheduledTaskState.Disabled);

        Assert.Equal(TweakStatus.Applied, await Build(partial).GetStatusAsync());
    }

    /// <summary>
    /// A task the user disabled themselves has no journal entry, so the revert leaves it alone.
    /// Switching it back on would undo a decision that was never ours to undo.
    /// </summary>
    [Fact]
    public async Task Reverting_only_switches_on_what_this_app_switched_off()
    {
        var tasks = new FakeScheduledTaskService()
            .With(Appraiser, ScheduledTaskState.Ready)
            .With(ProgramData, ScheduledTaskState.Ready)
            .With(UsbCeip, ScheduledTaskState.Disabled);     // off before SysTuneX ever ran

        TelemetryTaskTweakHandler handler = Build(tasks);

        await handler.ApplyAsync();
        tasks.Changes.Clear();

        await handler.RevertAsync();

        Assert.Equal(
            [Appraiser, ProgramData],
            tasks.Changes.Select(change => change.Path).Order(StringComparer.Ordinal));

        Assert.All(tasks.Changes, change => Assert.True(change.Enabled));
    }

    [Fact]
    public async Task Applying_it_does_not_touch_a_task_that_is_already_off()
    {
        var tasks = new FakeScheduledTaskService()
            .With(Appraiser, ScheduledTaskState.Ready)
            .With(UsbCeip, ScheduledTaskState.Disabled);

        await Build(tasks).ApplyAsync();

        Assert.Equal([Appraiser], tasks.Changes.Select(change => change.Path));
    }

    /// <summary>A task that is running right now is on, and gets switched off like any other.</summary>
    [Fact]
    public async Task A_running_task_is_switched_off_too()
    {
        var tasks = new FakeScheduledTaskService().With(Appraiser, ScheduledTaskState.Running);

        await Build(tasks).ApplyAsync();

        Assert.Equal([Appraiser], tasks.Changes.Select(change => change.Path));
    }

    [Fact]
    public async Task Everything_already_off_is_not_a_second_change()
    {
        var tasks = new FakeScheduledTaskService().With(Appraiser, ScheduledTaskState.Disabled);

        OperationResult result = await Build(tasks).ApplyAsync();

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Empty(tasks.Changes);
    }

    /// <summary>
    /// Apply with nothing readable must not report success. This is the failure the Unknown status
    /// exists to prevent, checked on the path that actually changes the machine.
    /// </summary>
    [Fact]
    public async Task Applying_it_when_nothing_can_be_read_fails()
    {
        var tasks = new FakeScheduledTaskService { QueryFails = true };

        OperationResult result = await Build(tasks).ApplyAsync();

        Assert.False(result.Success);
        Assert.Empty(tasks.Changes);
    }

    /// <summary>
    /// Every path in the catalogue is already in the spelling the query normalises to. A path with
    /// a stray double backslash would never match what Windows reports, and the tweak would look
    /// permanently unavailable.
    /// </summary>
    [Fact]
    public void Every_catalogued_task_path_is_already_normalised()
    {
        Assert.All(
            TelemetryTaskTweakHandler.Tasks,
            path => Assert.Equal(path, ScheduledTaskQuery.Normalize(path)));
    }
}
