using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Who is sitting at the desktop this process shows its window on.
///
/// A service of its own rather than a member of <see cref="IEnvironmentService"/> because the
/// registry needs it: the environment reads the Windows version through the registry, so the
/// registry cannot depend on the environment in return.
/// </summary>
public interface IDesktopSession
{
    /// <summary>
    /// Set when SysTuneX runs as a different account from the desktop's; null when they are the
    /// same, or when it could not be told - in which case nothing is redirected, as before.
    /// </summary>
    SeparateElevation? SeparateElevation { get; }
}
