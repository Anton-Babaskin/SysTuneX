using Microsoft.Win32;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Registry access, always against the 64-bit view so a 32-bit host process cannot be
/// silently redirected into Wow6432Node.
/// </summary>
public interface IRegistryService
{
    /// <summary>Reads a value, or <see langword="null"/> when the key or value does not exist.</summary>
    object? GetValue(string keyPath, string valueName);

    /// <summary>Reads a value together with its registry type.</summary>
    (object? Value, RegistryValueKind Kind) GetValueWithKind(string keyPath, string valueName);

    /// <summary>Writes a value, creating the key if needed, and verifies the write by reading it back.</summary>
    OperationResult SetValue(string keyPath, string valueName, object value, RegistryValueKind kind);

    /// <summary>Deletes a value. Missing values are not an error.</summary>
    OperationResult DeleteValue(string keyPath, string valueName);

    bool KeyExists(string keyPath);

    bool ValueExists(string keyPath, string valueName);

    /// <summary>Sub-key names under <paramref name="keyPath"/>, empty when the key is missing.</summary>
    IReadOnlyList<string> GetSubKeyNames(string keyPath);
}
