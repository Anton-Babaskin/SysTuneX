using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Store apps that can be removed.
///
/// Split from <c>ICleanupService</c>, which deletes cached files. The two were one interface and
/// one class doing two unrelated things through two unrelated mechanisms - deleting directories,
/// and driving PowerShell's package manager - and the same split had already appeared by itself in
/// the cleanup page, which shows them as two separate lists.
/// </summary>
public interface IAppPackageService
{
    /// <summary>
    /// Removable packages this machine actually has, from the curated list. Windows is not asked
    /// for everything it could remove: a list of every installed package would include things the
    /// user needs, presented as things to get rid of.
    /// </summary>
    Task<IReadOnlyList<AppPackage>> GetRemovableAppsAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> RemoveAppAsync(string packageName, CancellationToken cancellationToken = default);
}
