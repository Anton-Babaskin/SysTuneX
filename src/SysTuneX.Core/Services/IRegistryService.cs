namespace SysTuneX.Core.Services;

public interface IRegistryService
{
    object? GetValue(string keyPath, string valueName);
    bool SetValue(string keyPath, string valueName, object value, Microsoft.Win32.RegistryValueKind kind);
    bool DeleteValue(string keyPath, string valueName);
    bool KeyExists(string keyPath);
    bool ValueExists(string keyPath, string valueName);
}
