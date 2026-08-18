using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The backup journal is the whole basis of the rollback promise, so its semantics are pinned
/// down here: record once, never overwrite the machine's genuine original, survive a reload.
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly FakeEnvironment _environment;

    public BackupServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "systunex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _environment = new FakeEnvironment(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }

    private BackupService CreateService() => new(NullLogger<BackupService>.Instance, _environment);

    [Fact]
    public async Task Records_the_value_that_was_there_before()
    {
        BackupService backup = CreateService();

        await backup.RecordRegistryAsync("tweak:example", @"HKCU\Software\Test", "Value", 20, RegistryValueKind.DWord);

        BackupEntry? entry = backup.FindActive(BackupKind.RegistryValue, @"HKCU\Software\Test", "Value");

        Assert.NotNull(entry);
        Assert.Equal("20", entry.OriginalValue);
        Assert.Equal(RegistryValueKind.DWord, entry.OriginalValueKind);
        Assert.True(entry.IsActive);
    }

    [Fact]
    public async Task A_missing_value_is_recorded_as_absent_rather_than_as_zero()
    {
        BackupService backup = CreateService();

        await backup.RecordRegistryAsync("tweak:example", @"HKCU\Software\Test", "Missing", null, RegistryValueKind.Unknown);

        BackupEntry? entry = backup.FindActive(BackupKind.RegistryValue, @"HKCU\Software\Test", "Missing");

        // Null means "delete on revert". Recording it as 0 would leave a value Windows never had.
        Assert.NotNull(entry);
        Assert.Null(entry.OriginalValue);
    }

    [Fact]
    public async Task Re_applying_a_tweak_does_not_overwrite_the_original_value()
    {
        BackupService backup = CreateService();

        await backup.RecordRegistryAsync("tweak:example", @"HKCU\Software\Test", "Value", 20, RegistryValueKind.DWord);

        // Second apply: the current value is now SysTuneX's own, and must not replace the record.
        await backup.RecordRegistryAsync("tweak:example", @"HKCU\Software\Test", "Value", 10, RegistryValueKind.DWord);

        BackupEntry? entry = backup.FindActive(BackupKind.RegistryValue, @"HKCU\Software\Test", "Value");

        Assert.NotNull(entry);
        Assert.Equal("20", entry.OriginalValue);
        Assert.Single(backup.GetActive());
    }

    [Fact]
    public async Task Reverting_hides_the_entry_from_the_active_set_but_keeps_the_history()
    {
        BackupService backup = CreateService();

        await backup.RecordRegistryAsync("tweak:example", @"HKCU\Software\Test", "Value", 20, RegistryValueKind.DWord);
        BackupEntry entry = backup.GetActive().Single();

        await backup.MarkRevertedAsync([entry.Id]);

        Assert.Empty(backup.GetActive());
        Assert.Single(backup.GetAll());
        Assert.NotNull(backup.GetAll().Single().RevertedAt);
    }

    [Fact]
    public async Task A_reverted_target_can_be_recorded_again()
    {
        BackupService backup = CreateService();

        await backup.RecordRegistryAsync("tweak:example", @"HKCU\Software\Test", "Value", 20, RegistryValueKind.DWord);
        await backup.MarkRevertedAsync(BackupKind.RegistryValue, @"HKCU\Software\Test", "Value");
        await backup.RecordRegistryAsync("tweak:example", @"HKCU\Software\Test", "Value", 20, RegistryValueKind.DWord);

        Assert.Single(backup.GetActive());
        Assert.Equal(2, backup.GetAll().Count);
    }

    [Fact]
    public async Task The_journal_survives_a_restart()
    {
        BackupService first = CreateService();
        await first.RecordServiceAsync("service:SysMain", "SysMain", ServiceStartMode.Automatic, wasRunning: true);

        BackupService second = CreateService();
        await second.LoadAsync();

        BackupEntry? entry = second.FindActive(BackupKind.ServiceConfiguration, "SysMain");

        Assert.NotNull(entry);
        Assert.Equal(ServiceStartMode.Automatic, entry.OriginalStartMode);
        Assert.True(entry.OriginalWasRunning);
    }

    [Fact]
    public async Task A_corrupt_journal_does_not_stop_the_app_from_starting()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "backup.json"), "{ this is not json");

        BackupService backup = CreateService();
        await backup.LoadAsync();

        Assert.Empty(backup.GetAll());
        Assert.NotEmpty(Directory.GetFiles(_directory, "backup.json.corrupt-*"));
    }

    [Fact]
    public async Task Different_values_under_the_same_key_are_tracked_separately()
    {
        BackupService backup = CreateService();

        await backup.RecordRegistryAsync("tweak:a", @"HKCU\Software\Test", "One", 1, RegistryValueKind.DWord);
        await backup.RecordRegistryAsync("tweak:a", @"HKCU\Software\Test", "Two", 2, RegistryValueKind.DWord);

        Assert.Equal(2, backup.GetActive().Count);
        Assert.Equal("1", backup.FindActive(BackupKind.RegistryValue, @"HKCU\Software\Test", "One")!.OriginalValue);
        Assert.Equal("2", backup.FindActive(BackupKind.RegistryValue, @"HKCU\Software\Test", "Two")!.OriginalValue);
    }

    [Fact]
    public async Task Dns_records_distinguish_dhcp_from_a_static_list()
    {
        BackupService backup = CreateService();

        await backup.RecordDnsAsync("network:dns", "{adapter-1}", usedDhcp: true, []);
        await backup.RecordDnsAsync("network:dns", "{adapter-2}", usedDhcp: false, ["10.0.0.1", "10.0.0.2"]);

        Assert.Equal("dhcp", backup.FindActive(BackupKind.DnsConfiguration, "{adapter-1}")!.OriginalValue);
        Assert.Equal("10.0.0.1,10.0.0.2", backup.FindActive(BackupKind.DnsConfiguration, "{adapter-2}")!.OriginalValue);
    }

    private sealed class FakeEnvironment : IEnvironmentService
    {
        public FakeEnvironment(string dataDirectory) => DataDirectory = dataDirectory;

        public bool IsElevated => true;

        public WindowsVersionInfo Windows { get; } = new()
        {
            Major = 10,
            Minor = 0,
            Build = 26100,
            ProductName = "Windows 11 Pro",
        };

        public string DataDirectory { get; }

        public OperationResult RestartElevated() => OperationResult.Ok();

        public Task<OperationResult> RestartExplorerAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Ok());
    }
}
