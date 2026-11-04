using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using SysTuneX.Core.Tweaks;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The change journal is replayed as administrator, so what it names is checked first.
///
/// Restore All used to act on whatever the journal said: write any registry value in any hive, enable
/// any scheduled task path, put back and start any service. The journal is a file, so that made it a
/// way to get anything done with the administrator's token by anyone who could edit it. Each entry is
/// now held to what SysTuneX itself could have recorded, and one that fails is reported and skipped -
/// without taking the rest of the restore with it.
/// </summary>
public sealed class JournalReplayTests
{
    private const string RunKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static readonly RegistryChange CatalogueValue = TweakCatalog.All.SelectMany(t => t.Changes).First();

    private static string AdapterKey(Guid adapter) => NagleTweakHandler.InterfacesKey + "\\" + adapter.ToString("B");

    // ── What counts as a target SysTuneX could have recorded ──────────────────────────────

    [Fact]
    public void Every_value_the_catalogue_writes_is_accepted()
    {
        List<string> refused =
        [
            .. TweakCatalog.All
                .SelectMany(t => t.Changes)
                .Where(c => !JournalTargets.IsKnownRegistryValue(c.KeyPath, c.ValueName))
                .Select(c => $@"{c.KeyPath}\{c.ValueName}"),
        ];

        Assert.Empty(refused);
    }

    [Theory]
    [InlineData("TcpAckFrequency")]
    [InlineData("TCPNoDelay")]
    [InlineData("tcpnodelay")]
    public void The_values_the_nagle_handler_writes_under_an_adapter_are_accepted(string valueName)
    {
        Assert.True(JournalTargets.IsKnownRegistryValue(AdapterKey(Guid.NewGuid()), valueName));
    }

    [Theory]
    [InlineData(RunKey, "Updater")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\sethc.exe", "Debugger")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\not-an-adapter", "TCPNoDelay")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{2b7c1f3e-4a5d-4c6b-9e8f-0a1b2c3d4e5f}", "NameServer")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{2b7c1f3e-4a5d-4c6b-9e8f-0a1b2c3d4e5f}\Sub", "TCPNoDelay")]
    [InlineData("", "")]
    public void A_value_SysTuneX_never_writes_is_refused(string keyPath, string valueName)
    {
        Assert.False(JournalTargets.IsKnownRegistryValue(keyPath, valueName));
    }

    /// <summary>The key alone is not enough: the value has to be one the catalogue writes there.</summary>
    [Fact]
    public void Another_value_under_a_catalogue_key_is_refused()
    {
        Assert.False(JournalTargets.IsKnownRegistryValue(CatalogueValue.KeyPath, CatalogueValue.ValueName + "-other"));
        Assert.False(JournalTargets.IsKnownRegistryValue(CatalogueValue.KeyPath + @"\Sub", CatalogueValue.ValueName));
    }

    [Fact]
    public void A_missing_registry_target_is_refused()
    {
        Assert.False(JournalTargets.IsKnownRegistryValue(null, CatalogueValue.ValueName));
        Assert.False(JournalTargets.IsKnownRegistryValue(CatalogueValue.KeyPath, null));
    }

    [Fact]
    public void Every_task_the_telemetry_tweak_switches_off_is_a_valid_path()
    {
        Assert.All(TelemetryTaskTweakHandler.Tasks, path => Assert.True(JournalTargets.IsScheduledTaskPath(path)));
    }

    [Theory]
    [InlineData(@"\RootTask")]
    [InlineData(@"\Vendor\Updater Task")]
    [InlineData("\\Vendor\\Bob\u2019s task")]
    [InlineData(@"\Vendor\Bob's task")]
    public void A_well_formed_task_path_is_accepted(string path)
    {
        Assert.True(JournalTargets.IsScheduledTaskPath(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"\")]
    [InlineData(@"Microsoft\Windows\Autochk\Proxy")]
    [InlineData(@"\Microsoft\\Proxy")]
    [InlineData(@"\Microsoft\Proxy\")]
    [InlineData(@"\Microsoft\..\Proxy")]
    [InlineData(@"\Microsoft\.\Proxy")]
    [InlineData(@"\Microsoft\Pro""xy")]
    [InlineData(@"\Microsoft\Pro|xy")]
    [InlineData(@"\Microsoft\Pro:xy")]
    [InlineData(@"\Microsoft\Pro*")]
    [InlineData(@"\Microsoft\Pro?")]
    [InlineData(@"\Microsoft\<Proxy>")]
    [InlineData(@"\Microsoft/Proxy")]
    [InlineData("\\Microsoft\\Pro\nxy")]
    [InlineData("\\Microsoft\\Pro\txy")]
    public void A_malformed_task_path_is_refused(string path)
    {
        Assert.False(JournalTargets.IsScheduledTaskPath(path));
    }

    [Fact]
    public void A_missing_task_path_is_refused()
    {
        Assert.False(JournalTargets.IsScheduledTaskPath(null));
    }

    [Fact]
    public void Every_service_in_the_catalogue_is_accepted()
    {
        Assert.All(ServiceCatalog.All, service => Assert.True(JournalTargets.IsKnownService(service.ServiceName)));
    }

    [Theory]
    [InlineData("diagtrack")]
    [InlineData("CDPUserSvc_4f2a1")]
    [InlineData("OneSyncSvc_1d6a7c3")]
    public void A_catalogue_service_or_a_per_user_instance_of_one_is_accepted(string serviceName)
    {
        Assert.True(JournalTargets.IsKnownService(serviceName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("TermService")]
    [InlineData("SomethingElse")]
    [InlineData("DiagTrack_4f2a1")]
    [InlineData("CDPUserSvc_")]
    [InlineData("CDPUserSvc_xyz")]
    [InlineData("CDPUserSvc_4f2a1_4f2a1")]
    [InlineData("CDPUserSvc_12345678901234567")]
    [InlineData("_4f2a1")]
    public void A_service_SysTuneX_does_not_manage_is_refused(string serviceName)
    {
        Assert.False(JournalTargets.IsKnownService(serviceName));
    }

    [Fact]
    public void A_missing_service_name_is_refused()
    {
        Assert.False(JournalTargets.IsKnownService(null));
    }

    // ── The restorers act on what passes and report what does not ────────────────────────────

    [Fact]
    public async Task The_registry_restorer_writes_catalogue_values_and_leaves_any_other_alone()
    {
        var trace = new CallTrace();
        var registry = new TracingRegistryService(trace);
        FakeBackupService backup = new FakeBackupService()
            .Record(RunKey, "Updater", @"C:\Users\Public\updater.exe", RegistryValueKind.String)
            .Record(CatalogueValue.KeyPath, CatalogueValue.ValueName, originalValue: null)
            .Record(AdapterKey(Guid.NewGuid()), "TcpAckFrequency", "2");

        IReadOnlyList<BackupEntry> entries = backup.GetActive();

        RestoreOutcome outcome = await new RegistryChangeRestorer(
                registry,
                backup,
                NullLogger<RegistryChangeRestorer>.Instance)
            .RestoreAsync(entries);

        // The refused entry came first and the two after it were still put back.
        Assert.Equal(2, outcome.Changed);
        Assert.Equal(1, outcome.Failed);
        Assert.Contains(RunKey, Assert.Single(outcome.Errors), StringComparison.Ordinal);
        Assert.DoesNotContain(trace.Calls, call => call.Contains(RunKey, StringComparison.OrdinalIgnoreCase));

        // Left active, so the history still shows it rather than calling it restored.
        Assert.Equal([entries[1].Id, entries[2].Id], backup.RevertedIds);
    }

    [Fact]
    public async Task The_scheduled_task_restorer_enables_valid_paths_and_leaves_malformed_ones_alone()
    {
        var tasks = new FakeScheduledTaskService();
        var backup = new FakeBackupService();

        await backup.RecordRawAsync(TaskEntry(@"Microsoft\Windows\NoLeadingSlash"));
        await backup.RecordRawAsync(TaskEntry(@"\Microsoft\Windows\Autochk\Proxy"));
        await backup.RecordRawAsync(TaskEntry(@"\Microsoft\Windows\Pro|xy"));

        IReadOnlyList<BackupEntry> entries = backup.GetActive();

        RestoreOutcome outcome = await new ScheduledTaskChangeRestorer(
                tasks,
                backup,
                NullLogger<ScheduledTaskChangeRestorer>.Instance)
            .RestoreAsync(entries);

        Assert.Equal([(@"\Microsoft\Windows\Autochk\Proxy", true)], tasks.Changes);
        Assert.Equal(1, outcome.Changed);
        Assert.Equal(2, outcome.Failed);
        Assert.Equal(2, outcome.Errors.Count);
        Assert.Equal([entries[1].Id], backup.RevertedIds);
    }

    [Fact]
    public async Task The_scheduled_task_restorer_does_not_start_PowerShell_when_nothing_is_valid()
    {
        var tasks = new FakeScheduledTaskService();
        var backup = new FakeBackupService();
        await backup.RecordRawAsync(TaskEntry("not a task path"));

        RestoreOutcome outcome = await new ScheduledTaskChangeRestorer(
                tasks,
                backup,
                NullLogger<ScheduledTaskChangeRestorer>.Instance)
            .RestoreAsync(backup.GetActive());

        Assert.Empty(tasks.Changes);
        Assert.Equal(0, outcome.Changed);
        Assert.Equal(1, outcome.Failed);
        Assert.Empty(backup.RevertedIds);
    }

    [Fact]
    public async Task The_service_restorer_puts_back_managed_services_and_leaves_any_other_alone()
    {
        var services = new FakeServiceManager();
        var backup = new FakeBackupService();

        foreach (string name in new[] { "TermService", "DiagTrack", "CDPUserSvc_4f2a1", "SomethingElse" })
        {
            await backup.RecordRawAsync(new BackupEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = BackupKind.ServiceConfiguration,
                Target = name,
                OriginalStartMode = ServiceStartMode.Automatic,
                OriginalWasRunning = true,
            });
        }

        RestoreOutcome outcome = await new ServiceChangeRestorer(services, NullLogger<ServiceChangeRestorer>.Instance)
            .RestoreAsync(backup.GetActive());

        Assert.Equal(["DiagTrack", "CDPUserSvc_4f2a1"], services.Restored);
        Assert.Equal(2, outcome.Changed);
        Assert.Equal(2, outcome.Failed);
        Assert.Contains(outcome.Errors, error => error.StartsWith("TermService", StringComparison.Ordinal));
        Assert.Contains(outcome.Errors, error => error.StartsWith("SomethingElse", StringComparison.Ordinal));
    }

    private static BackupEntry TaskEntry(string path) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Kind = BackupKind.ScheduledTask,
        Target = path,
        OriginalValue = nameof(ScheduledTaskState.Ready),
    };
}
