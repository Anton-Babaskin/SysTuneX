namespace SysTuneX.Core.Services;

public interface IPrivacyService
{
    bool BlockTelemetryHosts();
    bool UnblockTelemetryHosts();
    bool AreHostsBlocked();
}
