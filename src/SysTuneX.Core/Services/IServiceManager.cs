using SysTuneX.Core.Models;

namespace SysTuneX.Core.Services;

public interface IServiceManager
{
    List<ServiceItem> GetManagedServices();
    bool IsServiceRunning(string serviceName);
    bool StopService(string serviceName);
    bool StartService(string serviceName);
    bool SetServiceStartType(string serviceName, bool disabled);
}
