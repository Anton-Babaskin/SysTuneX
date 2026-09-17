using System.Globalization;
using System.Runtime.Versioning;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <summary>
/// PCI Express link state power management, off.
///
/// ASPM lets the chipset put a PCIe link into a low-power state between transfers and wake it on
/// the next one. Waking takes microseconds, which is nothing - until it happens between a mouse
/// report and the frame that should have used it, on a link carrying the NVMe drive a game is
/// streaming from or the network card an online match is on. It is one of the few power settings
/// where "off" is a latency choice rather than a performance superstition.
///
/// It costs power. On a laptop on battery this is a real and measurable loss, which is why the
/// tweak says so rather than being sold as free.
///
/// Applied through powercfg, like core parking: the power manager reads the per-scheme index, and
/// nothing else.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PcieAspmTweakHandler : ISpecialTweakHandler
{
    /// <summary>PCI Express settings subgroup (SUB_PCIEXPRESS).</summary>
    private const string PciExpressSubgroup = "501a4d13-42af-4429-9fd1-a8218c268e20";

    /// <summary>Link State Power Management (ASPM).</summary>
    private const string LinkStateSetting = "ee12f906-d277-404b-b6da-e5fa1a576df5";

    /// <summary>0 off, 1 moderate power savings, 2 maximum power savings.</summary>
    private const int Off = 0;

    /// <summary>
    /// What Windows ships on every scheme but High Performance, and therefore the only defensible
    /// value to put back when nothing was recorded.
    /// </summary>
    private const int WindowsDefault = 2;

    private const string OwnerId = "tweak:pcie_aspm_disable";

    private readonly IPowerService _power;
    private readonly IBackupService _backup;

    public PcieAspmTweakHandler(IPowerService power, IBackupService backup)
    {
        _power = power;
        _backup = backup;
    }

    public string Key => "pcie_aspm";

    public async Task<TweakStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        int? current = await _power
            .GetSchemeSettingAsync(PciExpressSubgroup, LinkStateSetting, cancellationToken)
            .ConfigureAwait(false);

        // Null means powercfg would not answer, which is what a machine without the setting looks
        // like. Reporting that as NotApplied would offer the user a switch that cannot do anything.
        return current switch
        {
            null => TweakStatus.Unknown,
            Off => TweakStatus.Applied,
            _ => TweakStatus.NotApplied,
        };
    }

    public async Task<OperationResult> ApplyAsync(CancellationToken cancellationToken = default)
    {
        int? current = await _power
            .GetSchemeSettingAsync(PciExpressSubgroup, LinkStateSetting, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return OperationResult.Fail(CoreMessages.PowerSettingUnavailable);
        }

        if (current == Off)
        {
            return OperationResult.NoChange();
        }

        // Recorded before the change, so the revert puts back what this machine actually had
        // rather than the value a clean install would have - OEM schemes differ here.
        await _backup.RecordRawAsync(
                new BackupEntry
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = BackupKind.PowerSetting,
                    OwnerId = OwnerId,
                    Target = LinkStateSetting,
                    OriginalValue = current.Value.ToString(CultureInfo.InvariantCulture),
                },
                cancellationToken)
            .ConfigureAwait(false);

        return await _power
            .SetSchemeSettingAsync(
                PciExpressSubgroup, LinkStateSetting, Off, CoreMessages.PowerSettingRejected, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult> RevertAsync(CancellationToken cancellationToken = default)
    {
        BackupEntry? entry = _backup.FindActive(BackupKind.PowerSetting, LinkStateSetting);

        int target = entry?.OriginalValue is { } original &&
                     int.TryParse(original, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : WindowsDefault;

        OperationResult result = await _power
            .SetSchemeSettingAsync(
                PciExpressSubgroup, LinkStateSetting, target, CoreMessages.PowerSettingRejected, cancellationToken)
            .ConfigureAwait(false);

        if (result.Success && entry is not null)
        {
            await _backup.MarkRevertedAsync([entry.Id], cancellationToken).ConfigureAwait(false);
        }

        return result;
    }
}
