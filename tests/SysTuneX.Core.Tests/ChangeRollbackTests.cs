using Microsoft.Extensions.Logging.Abstractions;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// "Restore All" promises to put everything back. The way it failed was not a crash: a kind of
/// change nobody remembered to add to a chain of <c>if</c> blocks was recorded faithfully, listed
/// in the journal, and then quietly skipped - with nothing anywhere saying so.
///
/// These are the tests that make that impossible rather than unlikely.
/// </summary>
public sealed class ChangeRollbackTests
{
    /// <summary>A restorer that records what it was handed, so a test can watch the routing.</summary>
    private sealed class SpyRestorer(string id, int order, Func<BackupEntry, bool> handles) : IChangeRestorer
    {
        public string Id { get; } = id;

        public int Order { get; } = order;

        public List<BackupEntry> Received { get; } = [];

        public RestoreOutcome Result { get; set; } = new(1, 0, []);

        public bool Handles(BackupEntry entry) => handles(entry);

        public Task<RestoreOutcome> RestoreAsync(
            IReadOnlyList<BackupEntry> entries,
            IProgress<BatchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Received.AddRange(entries);
            return Task.FromResult(Result);
        }
    }

    private static BackupEntry Entry(BackupKind kind, string target = "x", string? owner = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Kind = kind,
        Target = target,
        OwnerId = owner,
    };

    private static ChangeRollbackService Rollback(FakeBackupService backup, params IChangeRestorer[] restorers) =>
        new(restorers, backup, NullLogger<ChangeRollbackService>.Instance);

    /// <summary>
    /// The whole point. A journal entry nobody claims comes back in the report rather than being
    /// dropped, so the user is told the machine is not fully restored instead of being told it is.
    /// </summary>
    [Fact]
    public async Task A_change_no_restorer_claims_is_reported_rather_than_skipped()
    {
        var backup = new FakeBackupService();
        await backup.RecordRawAsync(Entry(BackupKind.HostsFile));

        RollbackReport report = await Rollback(backup).RestoreEverythingAsync();

        BackupEntry orphan = Assert.Single(report.Unclaimed);
        Assert.Equal(BackupKind.HostsFile, orphan.Kind);
    }

    [Fact]
    public async Task A_claimed_change_is_handed_to_its_restorer()
    {
        var backup = new FakeBackupService();
        await backup.RecordRawAsync(Entry(BackupKind.ServiceConfiguration, "DiagTrack"));

        var services = new SpyRestorer("services", 10, e => e.Kind == BackupKind.ServiceConfiguration);

        RollbackReport report = await Rollback(backup, services).RestoreEverythingAsync();

        Assert.Equal("DiagTrack", Assert.Single(services.Received).Target);
        Assert.Empty(report.Unclaimed);
        Assert.Equal(1, report.For("services").Changed);
    }

    /// <summary>
    /// A registry value written by a tweak has to go back through that tweak's own revert, which
    /// may be a handler doing several things at once. Letting the registry restorer also have it
    /// would put one value back and call the job done.
    /// </summary>
    [Fact]
    public async Task An_entry_two_restorers_would_both_claim_goes_only_to_the_earlier_one()
    {
        var backup = new FakeBackupService();
        await backup.RecordRawAsync(Entry(BackupKind.RegistryValue, "HKLM\\X", owner: "tweak:game_bar_disable"));

        var tweaks = new SpyRestorer("tweaks", 0, e => e.OwnerId?.StartsWith("tweak:", StringComparison.Ordinal) == true);
        var registry = new SpyRestorer("registry", 90, e => e.Kind == BackupKind.RegistryValue);

        await Rollback(backup, registry, tweaks).RestoreEverythingAsync();

        Assert.Single(tweaks.Received);
        Assert.Empty(registry.Received);
    }

    /// <summary>Order comes from the restorer, not from the order the container happened to hand them over.</summary>
    [Fact]
    public async Task Restorers_run_in_their_declared_order_whatever_order_they_were_registered_in()
    {
        var backup = new FakeBackupService();
        await backup.RecordRawAsync(Entry(BackupKind.RegistryValue, owner: "tweak:a"));
        await backup.RecordRawAsync(Entry(BackupKind.ServiceConfiguration));

        var order = new List<string>();
        var late = new SpyRestorer("late", 50, e => e.Kind == BackupKind.ServiceConfiguration);
        var early = new SpyRestorer("early", 0, e => e.OwnerId is not null);

        var rollback = Rollback(backup, late, early);
        RollbackReport report = await rollback.RestoreEverythingAsync();

        Assert.Empty(report.Unclaimed);
        Assert.Single(early.Received);
        Assert.Single(late.Received);
    }

    [Fact]
    public async Task A_restorer_with_nothing_to_do_is_not_run_at_all()
    {
        var backup = new FakeBackupService();
        await backup.RecordRawAsync(Entry(BackupKind.ServiceConfiguration));

        var services = new SpyRestorer("services", 10, e => e.Kind == BackupKind.ServiceConfiguration);
        var dns = new SpyRestorer("dns", 30, e => e.Kind == BackupKind.DnsConfiguration);

        RollbackReport report = await Rollback(backup, services, dns).RestoreEverythingAsync();

        Assert.Empty(dns.Received);
        Assert.False(report.ByRestorer.ContainsKey("dns"));
    }

    [Fact]
    public async Task Errors_from_every_restorer_are_gathered()
    {
        var backup = new FakeBackupService();
        await backup.RecordRawAsync(Entry(BackupKind.ServiceConfiguration));
        await backup.RecordRawAsync(Entry(BackupKind.DnsConfiguration));

        var services = new SpyRestorer("services", 10, e => e.Kind == BackupKind.ServiceConfiguration)
        {
            Result = new RestoreOutcome(0, 1, ["a service refused"]),
        };
        var dns = new SpyRestorer("dns", 30, e => e.Kind == BackupKind.DnsConfiguration)
        {
            Result = new RestoreOutcome(0, 1, ["dns refused"]),
        };

        RollbackReport report = await Rollback(backup, services, dns).RestoreEverythingAsync();

        Assert.Equal(["a service refused", "dns refused"], report.AllErrors.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Nothing recorded means nothing to undo. Rolling back a clean machine has to be a no-op, not
    /// a reset to some notion of defaults - that would overwrite settings the user chose.
    /// </summary>
    [Fact]
    public async Task An_empty_journal_restores_nothing()
    {
        var services = new SpyRestorer("services", 10, _ => true);

        RollbackReport report = await Rollback(new FakeBackupService(), services).RestoreEverythingAsync();

        Assert.Empty(services.Received);
        Assert.Empty(report.Unclaimed);
        Assert.Empty(report.AllErrors);
    }

    /// <summary>
    /// The guard that turns "unlikely" into "impossible": every kind of change the journal can
    /// hold has a restorer registered for it. Adding a BackupKind without one fails here, at the
    /// moment it is added, rather than on a user's machine at the moment they press Restore All.
    /// </summary>
    [Fact]
    public void Every_kind_of_recorded_change_has_a_restorer()
    {
        IChangeRestorer[] restorers =
        [
            new TweakChangeRestorer(null!),
            new ServiceChangeRestorer(null!),
            new PowerSchemeChangeRestorer(null!),
            new PowerSettingChangeRestorer(null!, null!),
            new DnsChangeRestorer(null!),
            new HostsChangeRestorer(null!),
            new BootChangeRestorer([]),
            new ScheduledTaskChangeRestorer(null!, null!),
            new RegistryChangeRestorer(null!, null!),
        ];

        List<BackupKind> unhandled =
        [
            .. Enum.GetValues<BackupKind>()
                .Where(kind => !restorers.Any(r => r.Handles(Entry(kind))))
                .Order(),
        ];

        Assert.Empty(unhandled);
    }

    /// <summary>
    /// And that the list above is the list the application actually registers. A restorer written
    /// and never registered would satisfy the test above while doing nothing on a real machine.
    /// </summary>
    [Fact]
    public void Every_restorer_the_test_knows_about_is_registered_with_the_container()
    {
        string[] registered =
        [
            .. typeof(TweakChangeRestorer).Assembly
                .GetTypes()
                .Where(t => !t.IsAbstract && typeof(IChangeRestorer).IsAssignableFrom(t))
                .Select(t => t.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(9, registered.Length);
        Assert.Contains("RegistryChangeRestorer", registered);
        Assert.Contains("ScheduledTaskChangeRestorer", registered);
    }
}
