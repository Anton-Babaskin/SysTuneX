using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

public interface IStartupService
{
    List<StartupItem> GetStartupItems();
    bool SetEnabled(StartupItem item, bool enabled);
}
