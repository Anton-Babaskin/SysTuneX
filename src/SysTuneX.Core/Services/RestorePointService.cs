using System.Management;
using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Services;

public class RestorePointService : IRestorePointService
{
    private readonly ILogger<RestorePointService> _logger;

    public RestorePointService(ILogger<RestorePointService> logger)
    {
        _logger = logger;
    }

    public Task<bool> CreateAsync(string description)
    {
        return Task.Run(() =>
        {
            try
            {
                using var mc = new ManagementClass(@"\\localhost\root\default:SystemRestore");
                using var inParams = mc.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["RestorePointType"] = 0;  // APPLICATION_INSTALL
                inParams["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
                using var result = mc.InvokeMethod("CreateRestorePoint", inParams, null);
                var returnValue = (uint)(result?["ReturnValue"] ?? 1u);
                if (returnValue != 0)
                    _logger.LogWarning("CreateRestorePoint returned {Code}", returnValue);
                return returnValue == 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create restore point: {Description}", description);
                return false;
            }
        });
    }
}
