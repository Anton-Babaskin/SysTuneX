using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace SysTuneX.Core.Services;

public class RegistryService : IRegistryService
{
    private readonly ILogger<RegistryService> _logger;

    public RegistryService(ILogger<RegistryService> logger)
    {
        _logger = logger;
    }

    public object? GetValue(string keyPath, string valueName)
    {
        try
        {
            var (root, subKey) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(valueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read registry: {Path}\\{Name}", keyPath, valueName);
            return null;
        }
    }

    public bool SetValue(string keyPath, string valueName, object value, RegistryValueKind kind)
    {
        try
        {
            var (root, subKey) = ParseKeyPath(keyPath);
            using var key = root.CreateSubKey(subKey, writable: true);
            if (key == null) return false;
            key.SetValue(valueName, value, kind);

            // Verify the write
            var readBack = key.GetValue(valueName);
            var success = readBack?.ToString() == value.ToString();
            if (!success)
                _logger.LogWarning("Registry verify failed: {Path}\\{Name}", keyPath, valueName);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write registry: {Path}\\{Name}", keyPath, valueName);
            return false;
        }
    }

    public bool DeleteValue(string keyPath, string valueName)
    {
        try
        {
            var (root, subKey) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKey, writable: true);
            if (key == null) return true;
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete registry: {Path}\\{Name}", keyPath, valueName);
            return false;
        }
    }

    public bool KeyExists(string keyPath)
    {
        try
        {
            var (root, subKey) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKey);
            return key != null;
        }
        catch { return false; }
    }

    public bool ValueExists(string keyPath, string valueName)
    {
        try
        {
            var (root, subKey) = ParseKeyPath(keyPath);
            using var key = root.OpenSubKey(subKey);
            return key?.GetValue(valueName) != null;
        }
        catch { return false; }
    }

    private static (RegistryKey root, string subKey) ParseKeyPath(string keyPath)
    {
        var parts = keyPath.Split('\\', 2);
        var rootName = parts[0].ToUpperInvariant();
        var subKey = parts.Length > 1 ? parts[1] : string.Empty;

        RegistryKey root = rootName switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            "HKEY_USERS" or "HKU" => Registry.Users,
            _ => throw new ArgumentException($"Unknown registry root: {rootName}")
        };

        return (root, subKey);
    }
}
