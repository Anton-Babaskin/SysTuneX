namespace SysTuneX.Core.Services;

public interface ICleanupService
{
    long CleanTempFiles();
    long CleanWindowsTemp();
    long CleanPrefetch();
    long GetTotalCleanableSize();
    List<string> GetRemovableApps();
    bool RemoveApp(string packageName);
}
