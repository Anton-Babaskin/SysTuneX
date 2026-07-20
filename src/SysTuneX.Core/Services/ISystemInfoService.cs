using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

public interface ISystemInfoService
{
    HardwareInfo GetHardwareInfo();
    SystemSnapshot GetCurrentSnapshot();
}
