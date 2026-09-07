using System.IO;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Diagnostics;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IPowerService"/>
[SupportedOSPlatform("windows")]
public sealed partial class PowerService : IPowerService
{
    /// <summary>Processor power settings subgroup.</summary>
    private const string ProcessorSubgroup = "54533251-82be-4824-96c1-47b60b740d00";

    /// <summary>Processor performance core parking min cores.</summary>
    private const string MinimumCoresSetting = "0cc5b647-c1df-4637-891a-dec35c318583";

    /// <summary>Processor performance core parking max cores.</summary>
    private const string MaximumCoresSetting = "ea062031-0e34-4ff1-9b6d-eb1059334028";

    private const string OwnerId = "power:scheme";

    private readonly ILogger<PowerService> _logger;
    private readonly IBackupService _backup;

    public PowerService(ILogger<PowerService> logger, IBackupService backup)
    {
        _logger = logger;
        _backup = backup;
    }

    /// <summary>
    /// Matches a scheme GUID plus its display name in powercfg output. The surrounding labels are
    /// localised ("GUID схемы электропитания:" on a Russian install), so only the GUID and the
    /// parenthesised name are relied on.
    /// </summary>
    [GeneratedRegex(
        @"(?<guid>[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*(?:\((?<name>[^)]*)\))?\s*(?<active>\*)?",
        RegexOptions.ExplicitCapture)]
    private static partial Regex SchemeRegex();

    /// <summary>Matches the "Current AC Power Setting Index: 0x00000064" line of powercfg -q.</summary>
    [GeneratedRegex(@"0x(?<value>[0-9a-fA-F]{8})", RegexOptions.ExplicitCapture)]
    private static partial Regex HexValueRegex();

    public async Task<IReadOnlyList<PowerScheme>> GetSchemesAsync(CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await ProcessRunner
            .RunAsync("powercfg.exe", "/list", TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            _logger.LogWarning("powercfg /list failed: {Error}", result.Output);
            return [];
        }

        var schemes = new List<PowerScheme>();

        foreach (string line in result.StandardOutput.Split('\n'))
        {
            Match match = SchemeRegex().Match(line);
            if (!match.Success || !Guid.TryParse(match.Groups["guid"].Value, out Guid guid))
            {
                continue;
            }

            string name = match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : guid.ToString();
            schemes.Add(new PowerScheme(guid, name, match.Groups["active"].Success));
        }

        return schemes;
    }

    public async Task<PowerScheme?> GetActiveSchemeAsync(CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await ProcessRunner
            .RunAsync("powercfg.exe", "/getactivescheme", TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return null;
        }

        Match match = SchemeRegex().Match(result.StandardOutput);
        if (!match.Success || !Guid.TryParse(match.Groups["guid"].Value, out Guid guid))
        {
            return null;
        }

        string name = match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : guid.ToString();
        return new PowerScheme(guid, name, true);
    }

    public async Task<OperationResult> ActivateHighPerformanceAsync(CancellationToken cancellationToken = default)
    {
        PowerScheme? active = await GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);
        if (active is not null)
        {
            await _backup.RecordPowerSchemeAsync(OwnerId, active.Guid, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<PowerScheme> schemes = await GetSchemesAsync(cancellationToken).ConfigureAwait(false);

        Guid? target = schemes.FirstOrDefault(s => s.Guid == PowerScheme.UltimatePerformance)?.Guid
                       ?? schemes.FirstOrDefault(s => s.Guid == PowerScheme.HighPerformance)?.Guid;

        // A duplicate this app made on an earlier run, before minting another one.
        //
        // Windows hides Ultimate Performance until it is duplicated into the machine's scheme
        // list, and duplicatescheme mints a brand new GUID every time - so looking only for the
        // canonical GUID never found last run's copy and simply made another. Real logs show three
        // in a single day of use, from one source scheme: SysTuneX was littering the machine's
        // power settings with a new entry per game mode session and never removing any of them.
        target ??= RememberedDuplicate(schemes);

        if (target is null)
        {
            target = await DuplicateSchemeAsync(PowerScheme.UltimatePerformance, cancellationToken).ConfigureAwait(false)
                     ?? await DuplicateSchemeAsync(PowerScheme.HighPerformance, cancellationToken).ConfigureAwait(false);

            if (target is not null)
            {
                RememberDuplicate(target.Value);
            }
        }

        if (target is null)
        {
            return OperationResult.Fail(CoreMessages.PowerNoHighPerformanceScheme);
        }

        if (active?.Guid == target.Value)
        {
            return OperationResult.NoChange();
        }

        return await SetActiveSchemeAsync(target.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult> RestorePreviousSchemeAsync(CancellationToken cancellationToken = default)
    {
        BackupEntry? entry = _backup.FindActive(BackupKind.PowerScheme, "ActiveScheme");

        // Without a record, Balanced is the documented Windows default for a consumer install.
        Guid target = entry?.OriginalValue is { } value && Guid.TryParse(value, out Guid parsed)
            ? parsed
            : PowerScheme.Balanced;

        OperationResult result = await SetActiveSchemeAsync(target, cancellationToken).ConfigureAwait(false);

        if (result.Success && entry is not null)
        {
            await _backup.MarkRevertedAsync([entry.Id], cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<OperationResult> SetActiveSchemeAsync(Guid schemeGuid, CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await ProcessRunner
            .RunAsync("powercfg.exe", $"/setactive {schemeGuid:D}", TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return OperationResult.Fail(CoreMessages.PowerActivateFailed, result.Output.Trim());
        }

        _logger.LogInformation("Active power scheme set to {Guid}", schemeGuid);
        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetCoreParkingAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        // 100 percent minimum cores means the scheduler may never park a core.
        // The old build wrote ValueMax straight into the power settings key, which the power
        // manager ignores - the value has to go through powercfg and be re-activated.
        int minimumCores = enabled ? 5 : 100;
        var errors = new List<string>();

        foreach (string mode in new[] { "setacvalueindex", "setdcvalueindex" })
        {
            ProcessRunResult run = await ProcessRunner.RunAsync(
                    "powercfg.exe",
                    $"/{mode} SCHEME_CURRENT {ProcessorSubgroup} {MinimumCoresSetting} {minimumCores}",
                    TimeSpan.FromSeconds(10),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!run.Success)
            {
                errors.Add(run.Output.Trim());
            }

            ProcessRunResult maxRun = await ProcessRunner.RunAsync(
                    "powercfg.exe",
                    $"/{mode} SCHEME_CURRENT {ProcessorSubgroup} {MaximumCoresSetting} 100",
                    TimeSpan.FromSeconds(10),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!maxRun.Success)
            {
                errors.Add(maxRun.Output.Trim());
            }
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail(CoreMessages.PowerCoreParkingRejected, string.Join("; ", errors));
        }

        // The scheme has to be re-activated for the new indexes to take effect.
        ProcessRunResult reactivate = await ProcessRunner
            .RunAsync("powercfg.exe", "/setactive SCHEME_CURRENT", TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);

        return reactivate.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(CoreMessages.PowerReapplyFailed, reactivate.Output.Trim());
    }

    public async Task<bool> IsCoreParkingDisabledAsync(CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await ProcessRunner
            .RunAsync(
                "powercfg.exe",
                $"/q SCHEME_CURRENT {ProcessorSubgroup} {MinimumCoresSetting}",
                TimeSpan.FromSeconds(10),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return false;
        }

        // The AC index is the first "Current ... Setting Index" hex value in the output.
        foreach (string line in result.StandardOutput.Split('\n'))
        {
            if (!line.Contains("Index", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("индекс", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Match match = HexValueRegex().Match(line);
            if (match.Success && int.TryParse(
                    match.Groups["value"].Value,
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int value))
            {
                return value >= 100;
            }
        }

        return false;
    }

    public async Task<OperationResult> SetHibernationAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await ProcessRunner
            .RunAsync("powercfg.exe", $"/hibernate {(enabled ? "on" : "off")}", TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);

        return result.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(CoreMessages.PowerHibernationFailed, result.Output.Trim());
    }

    /// <summary>
    /// The scheme this app duplicated last time, if it is still on the machine.
    ///
    /// Matching by name would have to guess at a localised string; remembering the GUID we were
    /// given works in every language. A scheme the user has since deleted simply is not in the
    /// list, and a fresh one is made.
    /// </summary>
    private Guid? RememberedDuplicate(IReadOnlyList<PowerScheme> schemes)
    {
        try
        {
            if (!File.Exists(DuplicateNotePath))
            {
                return null;
            }

            string text = File.ReadAllText(DuplicateNotePath).Trim();

            return Guid.TryParse(text, out Guid remembered) && schemes.Any(s => s.Guid == remembered)
                ? remembered
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the remembered power scheme");
            return null;
        }
    }

    private void RememberDuplicate(Guid created)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DuplicateNotePath)!);
            File.WriteAllText(DuplicateNotePath, created.ToString("D"));
        }
        catch (Exception ex)
        {
            // Worst case this is forgotten and one more duplicate is made later; not worth failing over.
            _logger.LogDebug(ex, "Could not record the duplicated power scheme");
        }
    }

    private static string DuplicateNotePath =>
        Path.Combine(AppPaths.DataDirectory, "powerscheme.txt");

    private async Task<Guid?> DuplicateSchemeAsync(Guid source, CancellationToken cancellationToken)
    {
        ProcessRunResult result = await ProcessRunner
            .RunAsync("powercfg.exe", $"/duplicatescheme {source:D}", TimeSpan.FromSeconds(15), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            _logger.LogDebug("Could not duplicate power scheme {Guid}: {Error}", source, result.Output.Trim());
            return null;
        }

        Match match = SchemeRegex().Match(result.StandardOutput);
        if (match.Success && Guid.TryParse(match.Groups["guid"].Value, out Guid created))
        {
            _logger.LogInformation("Duplicated power scheme {Source} as {Created}", source, created);
            return created;
        }

        return null;
    }
}
