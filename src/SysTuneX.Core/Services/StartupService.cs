using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

public class StartupService : IStartupService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledSuffix = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run (disabled)";

    private readonly ILogger<StartupService> _logger;

    public StartupService(ILogger<StartupService> logger)
    {
        _logger = logger;
    }

    public List<StartupItem> GetStartupItems()
    {
        var items = new List<StartupItem>();
        items.AddRange(ReadFromHive(Registry.CurrentUser, "HKCU"));
        items.AddRange(ReadFromHive(Registry.LocalMachine, "HKLM"));
        return items;
    }

    public bool SetEnabled(StartupItem item, bool enabled)
    {
        try
        {
            var hive = item.Location == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
            if (enabled)
            {
                using var disabledKey = hive.OpenSubKey(DisabledSuffix, writable: true);
                var command = disabledKey?.GetValue(item.Name)?.ToString() ?? item.Command;
                disabledKey?.DeleteValue(item.Name, throwOnMissingValue: false);

                using var runKey = hive.CreateSubKey(RunKey, writable: true);
                runKey?.SetValue(item.Name, command);
            }
            else
            {
                using var runKey = hive.OpenSubKey(RunKey, writable: true);
                var command = runKey?.GetValue(item.Name)?.ToString() ?? item.Command;
                runKey?.DeleteValue(item.Name, throwOnMissingValue: false);

                using var disabledKey = hive.CreateSubKey(DisabledSuffix, writable: true);
                disabledKey?.SetValue(item.Name, command);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set startup item {Name} enabled={Enabled}", item.Name, enabled);
            return false;
        }
    }

    private static List<StartupItem> ReadFromHive(RegistryKey hive, string location)
    {
        var items = new List<StartupItem>();
        try
        {
            using var runKey = hive.OpenSubKey(RunKey);
            if (runKey != null)
            {
                foreach (var name in runKey.GetValueNames())
                {
                    items.Add(new StartupItem
                    {
                        Name = name,
                        Command = runKey.GetValue(name)?.ToString() ?? "",
                        Location = location,
                        IsEnabled = true,
                    });
                }
            }

            using var disabledKey = hive.OpenSubKey(DisabledSuffix);
            if (disabledKey != null)
            {
                foreach (var name in disabledKey.GetValueNames())
                {
                    if (items.Any(i => i.Name == name && i.Location == location))
                        continue;
                    items.Add(new StartupItem
                    {
                        Name = name,
                        Command = disabledKey.GetValue(name)?.ToString() ?? "",
                        Location = location,
                        IsEnabled = false,
                    });
                }
            }
        }
        catch { /* skip inaccessible hives */ }
        return items;
    }
}
