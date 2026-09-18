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

    /// <summary>
    /// How long to let a scheme switch take.
    ///
    /// Normally it is under a second. Ten seconds looked generous and was not: a real machine
    /// timed out on it, and killing powercfg mid-switch is the worst moment to give up. Thirty is
    /// long enough that a slow machine finishes and short enough that a genuinely stuck call still
    /// returns while the user is watching.
    /// </summary>
    private static readonly TimeSpan SetActiveTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<PowerService> _logger;
    private readonly IChangeJournalWriter _backup;
    private readonly IProcessRunner _processes;
    private readonly IEnvironmentService _environment;

    public PowerService(
        ILogger<PowerService> logger,
        IChangeJournalWriter backup,
        IProcessRunner processes,
        IEnvironmentService environment)
    {
        _logger = logger;
        _backup = backup;
        _processes = processes;
        _environment = environment;
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

    public async Task<IReadOnlyList<PowerScheme>> GetSchemesAsync(CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await _processes
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
        ProcessRunResult result = await _processes
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

    public async Task<bool> IsHighPerformanceActiveAsync(CancellationToken cancellationToken = default)
    {
        PowerScheme? active = await GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);

        if (active is null)
        {
            return false;
        }

        if (active.IsHighPerformance)
        {
            return true;
        }

        // The copy this app made itself. It carries a fresh GUID and the source scheme's name,
        // which is only the English string on an English Windows - so on every other machine the
        // note this app wrote is the only thing that identifies it.
        IReadOnlyList<PowerScheme> schemes = await GetSchemesAsync(cancellationToken).ConfigureAwait(false);

        return RememberedDuplicate(schemes) == active.Guid;
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

    /// <summary>
    /// Switches the active scheme, and checks before saying it failed.
    ///
    /// Two changes from the version that reported a timeout to a user whose scheme had in fact
    /// changed. The wait is longer, because ten seconds is fine on an idle desktop and not on a
    /// machine that is busy or has the setting under policy - and `powercfg /setactive` is a
    /// user-initiated action, so waiting is cheaper than being wrong.
    ///
    /// More importantly, a timeout is no longer taken as failure on its own. The run is killed
    /// when the clock runs out, but the switch may already have happened, so the active scheme is
    /// read back and believed over the stopwatch. Reporting "could not activate the scheme" about
    /// a machine that did activate it is the same class of lie this project refuses everywhere
    /// else, and it is the one the user actually hit.
    /// </summary>
    public async Task<OperationResult> SetActiveSchemeAsync(Guid schemeGuid, CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await _processes
            .RunAsync("powercfg.exe", $"/setactive {schemeGuid:D}", SetActiveTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (result.Success)
        {
            _logger.LogInformation("Active power scheme set to {Guid}", schemeGuid);
            return OperationResult.Ok();
        }

        PowerScheme? active = await GetActiveSchemeAsync(cancellationToken).ConfigureAwait(false);

        if (Activated(commandSucceeded: false, active?.Guid, schemeGuid))
        {
            _logger.LogInformation(
                "powercfg reported no success for {Guid} ({Output}), but the scheme is active - taking the machine's word for it",
                schemeGuid,
                result.Output.Trim());

            return OperationResult.Ok();
        }

        return OperationResult.Fail(CoreMessages.PowerActivateFailed, result.Output.Trim());
    }

    /// <summary>
    /// Whether the scheme ended up active, deciding between what the command said and what the
    /// machine says.
    ///
    /// Separated out because it is the judgement the timeout bug turned on, and because
    /// <see cref="ProcessRunner"/> is static, so the method around it cannot be tested. The rule:
    /// a command that succeeded is believed, and a command that did not is overruled only by the
    /// machine reporting the scheme we asked for. A scheme that could not be read back
    /// (<paramref name="activeAfterwards"/> null) is not evidence of anything and does not count.
    /// </summary>
    internal static bool Activated(bool commandSucceeded, Guid? activeAfterwards, Guid target) =>
        commandSucceeded || (activeAfterwards is { } actual && actual == target);

    public async Task<OperationResult> SetCoreParkingAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        // 100 percent minimum cores means the scheduler may never park a core.
        // The old build wrote ValueMax straight into the power settings key, which the power
        // manager ignores - the value has to go through powercfg and be re-activated.
        int minimumCores = enabled ? 5 : 100;

        OperationResult result = await SetSchemeSettingAsync(
                ProcessorSubgroup,
                MinimumCoresSetting,
                minimumCores,
                CoreMessages.PowerCoreParkingRejected,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return result;
        }

        return await SetSchemeSettingAsync(
                ProcessorSubgroup,
                MaximumCoresSetting,
                100,
                CoreMessages.PowerCoreParkingRejected,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsCoreParkingDisabledAsync(CancellationToken cancellationToken = default) =>
        await GetSchemeSettingAsync(ProcessorSubgroup, MinimumCoresSetting, cancellationToken)
            .ConfigureAwait(false) is >= 100;

    /// <summary>
    /// Writes one power setting on the active scheme, on mains and on battery, and re-activates
    /// the scheme so the new indexes take effect.
    ///
    /// Both halves matter. Writing only the AC index leaves a laptop unchanged the moment it is
    /// unplugged, and skipping the re-activation leaves the value stored and not in force - which
    /// is the failure the core parking code was written to fix in the first place.
    /// </summary>
    public async Task<OperationResult> SetSchemeSettingAsync(
        string subgroup,
        string setting,
        int value,
        MessageTemplate failureCode,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        foreach (string mode in new[] { "setacvalueindex", "setdcvalueindex" })
        {
            ProcessRunResult run = await _processes.RunAsync(
                    "powercfg.exe",
                    $"/{mode} SCHEME_CURRENT {subgroup} {setting} {value}",
                    TimeSpan.FromSeconds(10),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!run.Success)
            {
                errors.Add(run.Output.Trim());
            }
        }

        if (errors.Count > 0)
        {
            return OperationResult.Fail(failureCode, string.Join("; ", errors));
        }

        ProcessRunResult reactivate = await _processes
            .RunAsync("powercfg.exe", "/setactive SCHEME_CURRENT", TimeSpan.FromSeconds(10), cancellationToken)
            .ConfigureAwait(false);

        return reactivate.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(CoreMessages.PowerReapplyFailed, reactivate.Output.Trim());
    }

    /// <summary>
    /// Reads one power setting's current mains value, or null when powercfg will not answer -
    /// which happens when the setting is hidden on this machine rather than set to zero. The
    /// parsing lives in <see cref="PowerSettingIndex"/>, where a test can reach it.
    /// </summary>
    public async Task<int?> GetSchemeSettingAsync(
        string subgroup,
        string setting,
        CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await _processes
            .RunAsync(
                "powercfg.exe",
                $"/q SCHEME_CURRENT {subgroup} {setting}",
                TimeSpan.FromSeconds(10),
                cancellationToken)
            .ConfigureAwait(false);

        return result.Success ? PowerSettingIndex.Parse(result.StandardOutput) : null;
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

    private string DuplicateNotePath =>
        Path.Combine(_environment.DataDirectory, "powerscheme.txt");

    private async Task<Guid?> DuplicateSchemeAsync(Guid source, CancellationToken cancellationToken)
    {
        ProcessRunResult result = await _processes
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
