namespace SysTuneX.Core.Services;

public interface IRestorePointService
{
    Task<bool> CreateAsync(string description);
}
