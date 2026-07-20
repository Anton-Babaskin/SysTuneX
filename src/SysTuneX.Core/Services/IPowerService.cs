namespace SysTuneX.Core.Services;

public interface IPowerService
{
    bool ActivateUltimatePerformance();
    bool RestoreBalancedPlan();
    bool DisableHibernation();
    bool EnableHibernation();
    string GetActivePowerPlan();
    bool IsUltimatePerformanceActive();
}
