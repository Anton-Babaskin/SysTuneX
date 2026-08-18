using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Native;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="ITweakEngine"/>
[SupportedOSPlatform("windows")]
public sealed class TweakEngine : ITweakEngine
{
    private readonly ILogger<TweakEngine> _logger;
    private readonly IRegistryService _registry;
    private readonly IBackupService _backup;
    private readonly IEnvironmentService _environment;
    private readonly IReadOnlyDictionary<string, ISpecialTweakHandler> _handlers;

    // Handler status means shelling out to powercfg or bcdedit, which costs the better part
    // of a second. A short cache keeps a page refresh from paying that once per card.
    private readonly Dictionary<string, (TweakStatus Status, DateTime ReadAt)> _handlerStatusCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan HandlerStatusLifetime = TimeSpan.FromSeconds(10);

    public TweakEngine(
        ILogger<TweakEngine> logger,
        IRegistryService registry,
        IBackupService backup,
        IEnvironmentService environment,
        IEnumerable<ISpecialTweakHandler> handlers)
    {
        _logger = logger;
        _registry = registry;
        _backup = backup;
        _environment = environment;
        _handlers = handlers.ToDictionary(h => h.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<TweakDefinition> GetSupportedTweaks(TweakCategory? category = null)
    {
        WindowsVersionInfo windows = _environment.Windows;

        return TweakCatalog.All
            .Where(t => category is null || t.Category == category)
            .Where(t => t.AppliesTo(windows))
            .ToList();
    }

    public TweakDefinition? Find(string tweakId) => TweakCatalog.Find(tweakId);

    public TweakStatus GetStatus(TweakDefinition tweak)
    {
        if (!tweak.AppliesTo(_environment.Windows))
        {
            return TweakStatus.Unsupported;
        }

        if (tweak.HandlerKey is { } key)
        {
            if (!_handlers.TryGetValue(key, out ISpecialTweakHandler? handler))
            {
                return TweakStatus.Unknown;
            }

            lock (_handlerStatusCache)
            {
                if (_handlerStatusCache.TryGetValue(key, out (TweakStatus Status, DateTime ReadAt) cached) &&
                    DateTime.UtcNow - cached.ReadAt < HandlerStatusLifetime)
                {
                    return cached.Status;
                }
            }

            try
            {
                TweakStatus status = handler.GetStatusAsync()
                    .WaitAsync(TimeSpan.FromSeconds(12))
                    .GetAwaiter()
                    .GetResult();

                lock (_handlerStatusCache)
                {
                    _handlerStatusCache[key] = (status, DateTime.UtcNow);
                }

                return status;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Handler {Key} could not report its status", key);
                return TweakStatus.Unknown;
            }
        }

        int applied = 0;

        foreach (RegistryChange change in tweak.Changes)
        {
            object? current = _registry.GetValue(change.KeyPath, change.ValueName);

            if (RegistryValueComparer.AreEqual(current, change.OptimizedValue))
            {
                applied++;
            }
        }

        if (applied == tweak.Changes.Count)
        {
            return TweakStatus.Applied;
        }

        // A tweak that owns several values can end up half-written if one key is protected.
        // Reporting that honestly is more useful than rounding it down to "not applied".
        return applied == 0 ? TweakStatus.NotApplied : TweakStatus.Partial;
    }

    public async Task<OperationResult> ApplyAsync(TweakDefinition tweak, CancellationToken cancellationToken = default)
    {
        if (!tweak.AppliesTo(_environment.Windows))
        {
            return OperationResult.NoChange($"{tweak.Name} does not apply to Windows build {_environment.Windows.Build}.");
        }

        if (tweak.HandlerKey is { } key)
        {
            InvalidateHandlerStatus(key);

            return _handlers.TryGetValue(key, out ISpecialTweakHandler? handler)
                ? await handler.ApplyAsync(cancellationToken).ConfigureAwait(false)
                : OperationResult.Fail($"No handler is registered for '{key}'.");
        }

        var errors = new List<string>();
        bool changedAnything = false;

        foreach (RegistryChange change in tweak.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Record before writing. This is the line the whole rollback story depends on.
            (object? current, RegistryValueKind currentKind) = _registry.GetValueWithKind(change.KeyPath, change.ValueName);
            await _backup
                .RecordRegistryAsync($"tweak:{tweak.Id}", change.KeyPath, change.ValueName, current, currentKind, cancellationToken)
                .ConfigureAwait(false);

            OperationResult result = _registry.SetValue(change.KeyPath, change.ValueName, change.OptimizedValue, change.ValueKind);

            if (!result.Success)
            {
                errors.Add(result.Message ?? $"{change.KeyPath}\\{change.ValueName} could not be written.");
            }
            else if (result.Changed)
            {
                changedAnything = true;
            }
        }

        if (errors.Count == tweak.Changes.Count && errors.Count > 0)
        {
            return OperationResult.Fail(string.Join(" ", errors));
        }

        RunPostApply(tweak, applying: true);

        if (errors.Count > 0)
        {
            _logger.LogWarning("Tweak {Id} applied with errors: {Errors}", tweak.Id, string.Join("; ", errors));
            return OperationResult.Ok(string.Join(" ", errors));
        }

        _logger.LogInformation("Applied tweak {Id}", tweak.Id);
        return changedAnything ? OperationResult.Ok() : OperationResult.NoChange();
    }

    public async Task<OperationResult> RevertAsync(TweakDefinition tweak, CancellationToken cancellationToken = default)
    {
        if (tweak.HandlerKey is { } key)
        {
            InvalidateHandlerStatus(key);

            return _handlers.TryGetValue(key, out ISpecialTweakHandler? handler)
                ? await handler.RevertAsync(cancellationToken).ConfigureAwait(false)
                : OperationResult.Fail($"No handler is registered for '{key}'.");
        }

        var errors = new List<string>();
        var revertedEntries = new List<string>();
        bool changedAnything = false;

        foreach (RegistryChange change in tweak.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BackupEntry? entry = _backup.FindActive(BackupKind.RegistryValue, change.KeyPath, change.ValueName);
            OperationResult result;

            if (entry is not null)
            {
                // Restore what this machine actually had before SysTuneX touched it.
                if (entry.OriginalValue is null)
                {
                    result = _registry.DeleteValue(change.KeyPath, change.ValueName);
                }
                else
                {
                    object? restored = Materialize(entry.OriginalValue, entry.OriginalValueKind, change.ValueKind);
                    result = restored is null
                        ? _registry.DeleteValue(change.KeyPath, change.ValueName)
                        : _registry.SetValue(
                            change.KeyPath,
                            change.ValueName,
                            restored,
                            entry.OriginalValueKind == RegistryValueKind.Unknown ? change.ValueKind : entry.OriginalValueKind);
                }

                revertedEntries.Add(entry.Id);
            }
            else if (change.WindowsDefaultValue is null)
            {
                // Nothing recorded and Windows ships without the value: removing it is the honest revert.
                result = _registry.DeleteValue(change.KeyPath, change.ValueName);
            }
            else
            {
                result = _registry.SetValue(change.KeyPath, change.ValueName, change.WindowsDefaultValue, change.ValueKind);
            }

            if (!result.Success)
            {
                errors.Add(result.Message ?? $"{change.KeyPath}\\{change.ValueName} could not be restored.");
            }
            else if (result.Changed)
            {
                changedAnything = true;
            }
        }

        if (revertedEntries.Count > 0)
        {
            await _backup.MarkRevertedAsync(revertedEntries, cancellationToken).ConfigureAwait(false);
        }

        if (errors.Count == tweak.Changes.Count && errors.Count > 0)
        {
            return OperationResult.Fail(string.Join(" ", errors));
        }

        RunPostApply(tweak, applying: false);

        _logger.LogInformation("Reverted tweak {Id}", tweak.Id);
        return changedAnything ? OperationResult.Ok() : OperationResult.NoChange();
    }

    public Task<BatchResult> ApplyManyAsync(
        IEnumerable<TweakDefinition> tweaks,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunBatchAsync(tweaks, ApplyAsync, progress, cancellationToken);

    public Task<BatchResult> RevertManyAsync(
        IEnumerable<TweakDefinition> tweaks,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunBatchAsync(tweaks, RevertAsync, progress, cancellationToken);

    private async Task<BatchResult> RunBatchAsync(
        IEnumerable<TweakDefinition> tweaks,
        Func<TweakDefinition, CancellationToken, Task<OperationResult>> operation,
        IProgress<BatchProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<TweakDefinition> list = tweaks.ToList();
        int succeeded = 0;
        int failed = 0;
        int skipped = 0;
        bool requiresRestart = false;
        var errors = new List<string>();

        for (int i = 0; i < list.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TweakDefinition tweak = list[i];
            progress?.Report(new BatchProgress(tweak.Name, i, list.Count));

            if (!tweak.AppliesTo(_environment.Windows))
            {
                skipped++;
                continue;
            }

            OperationResult result;
            try
            {
                result = await operation(tweak, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = OperationResult.Fail($"{tweak.Name}: {ex.Message}", ex);
            }

            if (result.Success)
            {
                succeeded++;
                if (tweak.RequiresRestart && result.Changed)
                {
                    requiresRestart = true;
                }
            }
            else
            {
                failed++;
                errors.Add($"{tweak.Name}: {result.Message}");
            }
        }

        progress?.Report(new BatchProgress(string.Empty, list.Count, list.Count));

        return new BatchResult(succeeded, failed, skipped, errors) { RequiresRestart = requiresRestart };
    }

    private void InvalidateHandlerStatus(string key)
    {
        lock (_handlerStatusCache)
        {
            _handlerStatusCache.Remove(key);
        }
    }

    /// <summary>
    /// Pushes a change into the running session. Without this, tweaks like mouse acceleration
    /// and menu delay look like they did nothing until the next sign-out.
    /// </summary>
    private void RunPostApply(TweakDefinition tweak, bool applying)
    {
        if (tweak.PostApply == PostApplyAction.None)
        {
            return;
        }

        try
        {
            if (tweak.PostApply.HasFlag(PostApplyAction.RefreshMouseSettings))
            {
                NativeHelpers.ApplyMouseSettings(accelerationEnabled: !applying);
            }

            if (tweak.PostApply.HasFlag(PostApplyAction.RefreshVisualEffects))
            {
                NativeHelpers.ApplyUiEffects(enabled: !applying);
            }

            if (tweak.PostApply.HasFlag(PostApplyAction.BroadcastSettingChange))
            {
                NativeHelpers.BroadcastSettingChange();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Post-apply refresh for {Id} failed", tweak.Id);
        }
    }

    /// <summary>Turns a journal string back into a value of the right registry type.</summary>
    private static object? Materialize(string text, RegistryValueKind recordedKind, RegistryValueKind fallbackKind)
    {
        RegistryValueKind kind = recordedKind == RegistryValueKind.Unknown ? fallbackKind : recordedKind;

        return kind switch
        {
            RegistryValueKind.DWord => int.TryParse(text, out int dword) ? dword : null,
            RegistryValueKind.QWord => long.TryParse(text, out long qword) ? qword : null,
            RegistryValueKind.Binary => TryParseHex(text),
            RegistryValueKind.MultiString => text.Split(RegistryValueComparer.MultiStringSeparator, StringSplitOptions.RemoveEmptyEntries),
            _ => text,
        };
    }

    private static byte[]? TryParseHex(string text)
    {
        try
        {
            return Convert.FromHexString(text);
        }
        catch
        {
            return null;
        }
    }
}
