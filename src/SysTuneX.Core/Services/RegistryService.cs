using System.Runtime.Versioning;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IRegistryService"/>
[SupportedOSPlatform("windows")]
public sealed class RegistryService : IRegistryService
{
    private readonly ILogger<RegistryService> _logger;

    public RegistryService(ILogger<RegistryService> logger) => _logger = logger;

    public object? GetValue(string keyPath, string valueName) => GetValueWithKind(keyPath, valueName).Value;

    public (object? Value, RegistryValueKind Kind) GetValueWithKind(string keyPath, string valueName)
    {
        try
        {
            using RegistryKey? key = OpenKey(keyPath, writable: false);
            if (key is null)
            {
                return (null, RegistryValueKind.Unknown);
            }

            // Ask for the raw form so REG_EXPAND_SZ is preserved instead of being expanded on read.
            object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            RegistryValueKind kind = value is null ? RegistryValueKind.Unknown : key.GetValueKind(valueName);
            return (value, kind);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            _logger.LogDebug(ex, "Cannot read {Path}\\{Name}", keyPath, valueName);
            return (null, RegistryValueKind.Unknown);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected failure reading {Path}\\{Name}", keyPath, valueName);
            return (null, RegistryValueKind.Unknown);
        }
    }

    public OperationResult SetValue(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        try
        {
            (object? current, _) = GetValueWithKind(keyPath, valueName);
            if (RegistryValueComparer.AreEqual(current, value))
            {
                return OperationResult.NoChange();
            }

            using RegistryKey? key = CreateKey(keyPath);
            if (key is null)
            {
                return OperationResult.Fail($"Could not open or create {keyPath}.");
            }

            key.SetValue(valueName, value, kind);

            // Read back through a fresh handle so a cached view cannot mask a failed write.
            (object? written, _) = GetValueWithKind(keyPath, valueName);
            if (!RegistryValueComparer.AreEqual(written, value))
            {
                return OperationResult.Fail($"{keyPath}\\{valueName} did not keep the written value.");
            }

            _logger.LogInformation("Set {Path}\\{Name} = {Value}", keyPath, valueName, value);
            return OperationResult.Ok();
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail($"Access denied writing {keyPath}\\{valueName}. Run SysTuneX as administrator.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write {Path}\\{Name}", keyPath, valueName);
            return OperationResult.Fail($"Failed to write {keyPath}\\{valueName}: {ex.Message}", ex);
        }
    }

    public OperationResult DeleteValue(string keyPath, string valueName)
    {
        try
        {
            using RegistryKey? key = OpenKey(keyPath, writable: true);
            if (key is null)
            {
                return OperationResult.NoChange();
            }

            if (key.GetValue(valueName) is null)
            {
                return OperationResult.NoChange();
            }

            key.DeleteValue(valueName, throwOnMissingValue: false);
            _logger.LogInformation("Deleted {Path}\\{Name}", keyPath, valueName);
            return OperationResult.Ok();
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail($"Access denied deleting {keyPath}\\{valueName}.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {Path}\\{Name}", keyPath, valueName);
            return OperationResult.Fail($"Failed to delete {keyPath}\\{valueName}: {ex.Message}", ex);
        }
    }

    public bool KeyExists(string keyPath)
    {
        try
        {
            using RegistryKey? key = OpenKey(keyPath, writable: false);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public bool ValueExists(string keyPath, string valueName) => GetValue(keyPath, valueName) is not null;

    public IReadOnlyList<string> GetSubKeyNames(string keyPath)
    {
        try
        {
            using RegistryKey? key = OpenKey(keyPath, writable: false);
            return key?.GetSubKeyNames() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static RegistryKey? OpenKey(string keyPath, bool writable)
    {
        (RegistryHive hive, string subKey) = ParsePath(keyPath);

        // Registry64 keeps a 32-bit host process out of Wow6432Node.
        using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return root.OpenSubKey(subKey, writable);
    }

    private static RegistryKey? CreateKey(string keyPath)
    {
        (RegistryHive hive, string subKey) = ParsePath(keyPath);
        using RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return root.CreateSubKey(subKey, writable: true);
    }

    internal static (RegistryHive Hive, string SubKey) ParsePath(string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        string[] parts = keyPath.Split('\\', 2);
        string subKey = parts.Length > 1 ? parts[1] : string.Empty;

        RegistryHive hive = parts[0].ToUpperInvariant() switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => RegistryHive.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => RegistryHive.CurrentUser,
            "HKEY_CLASSES_ROOT" or "HKCR" => RegistryHive.ClassesRoot,
            "HKEY_USERS" or "HKU" => RegistryHive.Users,
            "HKEY_CURRENT_CONFIG" or "HKCC" => RegistryHive.CurrentConfig,
            _ => throw new ArgumentException($"Unknown registry root: {parts[0]}", nameof(keyPath)),
        };

        return (hive, subKey);
    }
}
