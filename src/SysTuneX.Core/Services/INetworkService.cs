namespace SysTuneX.Core.Services;

public interface INetworkService
{
    bool SetDns(string primary, string secondary);
    bool ResetDns();
    (string Primary, string Secondary) GetCurrentDns();
}
