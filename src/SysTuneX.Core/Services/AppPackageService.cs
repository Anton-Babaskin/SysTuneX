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
    /// </summary>
    private const string ListScript = """
        Get-AppxPackage |
            Where-Object { $_.NonRemovable -ne $true } |
            Select-Object Name, PackageFamilyName, Publisher |
            ConvertTo-Json -Compress
        """;

    private readonly ILogger<AppPackageService> _logger;
    private readonly IProcessRunner _processes;

    public AppPackageService(ILogger<AppPackageService> logger, IProcessRunner processes)
    {
        _logger = logger;
        _processes = processes;
    }

    public async Task<IReadOnlyList<AppPackage>> GetRemovableAppsAsync(CancellationToken cancellationToken = default)
    {
        ProcessRunResult result = await _processes
            .RunPowerShellAsync(ListScript, TimeSpan.FromSeconds(90), cancellationToken)
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
        // The name goes into a script, so anything that could end the string or start a second
        // statement is refused outright rather than escaped. Every name we pass comes from our own
        // catalogue; this is for the day one does not.
        if (string.IsNullOrWhiteSpace(packageName) || packageName.Any(c => c is '\'' or '"' or ';' or '|' or '&'))
        {
            return OperationResult.Fail(CoreMessages.CleanupUnsafePackageName);
        }

        string script = $$"""
            $ErrorActionPreference = 'Stop'
            Get-AppxPackage -Name '{{packageName}}' | Remove-AppxPackage
            """;

        ProcessRunResult result = await _processes
            .RunPowerShellAsync(script, TimeSpan.FromSeconds(120), cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return OperationResult.Fail(CoreMessages.CleanupPackageRemoveFailed, packageName, result.Output.Trim());
        }

        _logger.LogInformation("Removed Store package {Package}", packageName);
        return OperationResult.Ok();
    }
}
