using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="IAppPackageService"/>
[SupportedOSPlatform("windows")]
public sealed class AppPackageService : IAppPackageService
{
    /// <summary>
    /// Asks for JSON rather than loose text, and deliberately omits -AllUsers so the list matches
    /// what removing packages for the current user will actually affect.
    ///
    /// The current user is the desktop's. When Windows elevated SysTuneX into another account, a
    /// bare Get-AppxPackage lists that account's packages and Remove-AppxPackage removes them from
    /// that account, so the person at the screen kept every app the page said was gone. With a SID
    /// both act on the desktop account instead, which an administrator is allowed to do.
    /// </summary>
    private const string ListScript = """
        param($Sid)
        $(if ($Sid) { Get-AppxPackage -User $Sid } else { Get-AppxPackage }) |
            Where-Object { $_.NonRemovable -ne $true } |
            Select-Object Name, PackageFamilyName, Publisher |
            ConvertTo-Json -Compress
        """;

    private const string RemoveScript = """
        param($Name, $Sid)
        $ErrorActionPreference = 'Stop'
        if ($Sid) { Get-AppxPackage -Name $Name -User $Sid | Remove-AppxPackage -User $Sid }
        else { Get-AppxPackage -Name $Name | Remove-AppxPackage }
        """;

    private readonly ILogger<AppPackageService> _logger;
    private readonly IProcessRunner _processes;
    private readonly IDesktopSession _desktop;

    public AppPackageService(ILogger<AppPackageService> logger, IProcessRunner processes, IDesktopSession desktop)
    {
        _logger = logger;
        _processes = processes;
        _desktop = desktop;
    }

    /// <summary>The desktop account's SID when SysTuneX runs as another account; otherwise nothing is passed.</summary>
    private IReadOnlyList<string> DesktopUser =>
        _desktop.SeparateElevation is { } separate ? [separate.DesktopSid] : [];

    public async Task<IReadOnlyList<AppPackage>> GetRemovableAppsAsync(CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await _processes
            .RunPowerShellAsync(PowerShellScript.WithArguments(ListScript, DesktopUser), TimeSpan.FromSeconds(90), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            _logger.LogWarning("Could not enumerate Store packages: {Error}", result.Output.Trim());
            return [];
        }

        Dictionary<string, InstalledPackage> installed = InstalledPackages
            .Parse(result.StandardOutput)
            .ToDictionary(package => package.Name, StringComparer.OrdinalIgnoreCase);

        var packages = new List<AppPackage>();

        // The curated list, intersected with what is installed. Offering everything Windows would
        // let us remove would put things the user needs on a page headed "remove these".
        foreach (BloatwarePackage candidate in BloatwareCatalog.All)
        {
            if (!installed.TryGetValue(candidate.PackageName, out InstalledPackage found))
            {
                continue;
            }

            packages.Add(new AppPackage
            {
                PackageFamilyName = candidate.PackageName,
                DisplayName = candidate.DisplayName,
                Publisher = found.Publisher,
                IsSystemRelevant = candidate.IsSystemRelevant,
            });
        }

        return packages;
    }

    public async Task<OperationResult> RemoveAppAsync(string packageName, CancellationToken cancellationToken = default)
    {
        // No real package name contains any of these, so one that does is refused rather than
        // passed on. Every name we pass comes from our own catalogue; this is for the day one does
        // not. The name reaches the script as an argument, not as text: this list missed U+2018 to
        // U+201B, which PowerShell also reads as quotes, back when the name was quoted into it.
        if (string.IsNullOrWhiteSpace(packageName) || packageName.Any(c => c is '\'' or '"' or ';' or '|' or '&'))
        {
            return OperationResult.Fail(CoreMessages.CleanupUnsafePackageName);
        }

        ProcessRunResult result = await _processes
            .RunPowerShellAsync(
                PowerShellScript.WithArguments(RemoveScript, [packageName, .. DesktopUser]),
                TimeSpan.FromSeconds(120),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return OperationResult.Fail(CoreMessages.CleanupPackageRemoveFailed, packageName, result.Output.Trim());
        }

        _logger.LogInformation("Removed Store package {Package}", packageName);
        return OperationResult.Ok();
    }
}
