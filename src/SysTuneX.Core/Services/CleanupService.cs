using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
using SysTuneX.Core.Native;
using SysTuneX.Core.Tweaks;

namespace SysTuneX.Core.Services;

/// <inheritdoc cref="ICleanupService"/>
[SupportedOSPlatform("windows")]
public sealed class CleanupService : ICleanupService
{
    private readonly ILogger<CleanupService> _logger;
    private readonly IProcessRunner _processes;

    public CleanupService(ILogger<CleanupService> logger, IProcessRunner processes)
    {
        _logger = logger;
        _processes = processes;
    }

    public IReadOnlyList<CleanupTarget> GetTargets() =>
        CleanupCatalog.All.Where(t => ResolvePaths(t).Count > 0).ToList();

    public Task<CleanupScanResult> ScanAsync(CleanupTarget target, CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                IReadOnlyList<string> paths = ResolvePaths(target);
                long size = 0;
                int files = 0;
                DateTime cutoff = DateTime.UtcNow - target.MinimumAge;

                foreach (string path in paths)
                {
                    foreach (FileInfo file in EnumerateFiles(path, target.SearchPattern, cancellationToken))
                    {
                        if (target.MinimumAge > TimeSpan.Zero && file.LastWriteTimeUtc > cutoff)
                        {
                            continue;
                        }

                        size += file.Length;
                        files++;
                    }
                }

                return new CleanupScanResult
                {
                    TargetId = target.Id,
                    SizeBytes = size,
                    FileCount = files,
                    ResolvedPaths = paths,
                };
            },
            cancellationToken);
    }

    public Task<CleanupRunResult> CleanAsync(
        IEnumerable<CleanupTarget> targets,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<CleanupTarget> list = targets.ToList();

        return Task.Run(
            () =>
            {
                long freed = 0;
                int deleted = 0;
                int skipped = 0;
                var errors = new List<string>();

                for (int i = 0; i < list.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CleanupTarget target = list[i];
                    progress?.Report(new CleanupProgress(target.Name, i, list.Count, freed));

                    DateTime cutoff = DateTime.UtcNow - target.MinimumAge;

                    foreach (string path in ResolvePaths(target))
                    {
                        // Taken once, before the walk. Every delete below is checked against where
                        // this folder really was now, not against wherever its name leads by then.
                        if (ContainedDelete.ResolveRoot(path) is not { } root)
                        {
                            continue;
                        }

                        foreach (FileInfo file in EnumerateFiles(path, target.SearchPattern, cancellationToken))
                        {
                            if (target.MinimumAge > TimeSpan.Zero && file.LastWriteTimeUtc > cutoff)
                            {
                                skipped++;
                                continue;
                            }

                            try
                            {
                                long size = file.Length;

                                switch (ContainedDelete.Delete(file.FullName, root, directory: false))
                                {
                                    case ContainedDelete.Outcome.Deleted:
                                        freed += size;
                                        deleted++;
                                        break;

                                    case ContainedDelete.Outcome.OutsideRoot:
                                        _logger.LogWarning("Did not delete {File}: it leads outside {Root}", file.FullName, path);
                                        skipped++;
                                        break;

                                    default:
                                        // Locked by a running process: expected, not an error worth reporting.
                                        skipped++;
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"{file.FullName}: {ex.Message}");
                            }
                        }

                        if (target.RemoveEmptyDirectories)
                        {
                            RemoveEmptyDirectories(path, root, cancellationToken);
                        }
                    }
                }

                progress?.Report(new CleanupProgress(string.Empty, list.Count, list.Count, freed));
                _logger.LogInformation("Cleanup freed {Bytes} bytes across {Files} files", freed, deleted);

                return new CleanupRunResult
                {
                    FreedBytes = freed,
                    DeletedFiles = deleted,
                    SkippedFiles = skipped,
                    Errors = errors.Take(20).ToList(),
                };
            },
            cancellationToken);
    }

    private static IReadOnlyList<string> ResolvePaths(CleanupTarget target)
    {
        var resolved = new List<string>();

        foreach (string raw in target.Paths)
        {
            try
            {
                // Not just "exists": a root reached through a junction is skipped outright, so the
                // page never offers to sweep a folder the cleaner would then refuse to touch.
                string expanded = Environment.ExpandEnvironmentVariables(raw);
                if (ContainedDelete.ResolveRoot(expanded) is not null)
                {
                    resolved.Add(expanded);
                }
            }
            catch
            {
                // An unexpandable path simply does not apply to this machine.
            }
        }

        return resolved;
    }

    /// <summary>
    /// Walks a directory tree without letting one unreadable sub-directory abort the whole scan,
    /// which is what <c>SearchOption.AllDirectories</c> does.
    ///
    /// Junctions and symbolic links are not followed: they lead somewhere this target never named,
    /// and %SystemRoot%\Temp lets any user make one. That keeps the scan honest, but it is not what
    /// keeps the delete safe - a folder can become a link after this has listed it, which is why
    /// <see cref="ContainedDelete"/> checks every file again at the moment it deletes it.
    /// </summary>
    private static IEnumerable<FileInfo> EnumerateFiles(string root, string pattern, CancellationToken cancellationToken)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(root));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DirectoryInfo current = stack.Pop();

            DirectoryInfo[] subdirectories;
            try
            {
                subdirectories = current.GetDirectories();
            }
            catch
            {
                continue;
            }

            foreach (DirectoryInfo subdirectory in subdirectories)
            {
                if (!IsLink(subdirectory))
                {
                    stack.Push(subdirectory);
                }
            }

            FileInfo[] files;
            try
            {
                // Listed with their size and attributes, so nothing below goes back to the disk
                // for them - or fails because one vanished in between.
                files = current.GetFiles(pattern);
            }
            catch
            {
                continue;
            }

            foreach (FileInfo file in files)
            {
                if (!IsLink(file))
                {
                    yield return file;
                }
            }
        }
    }

    private static void RemoveEmptyDirectories(string directory, string resolvedRoot, CancellationToken cancellationToken)
    {
        try
        {
            foreach (DirectoryInfo child in new DirectoryInfo(directory).GetDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Neither into a link nor the link itself: what it points at was never ours to prune.
                if (IsLink(child))
                {
                    continue;
                }

                RemoveEmptyDirectories(child.FullName, resolvedRoot, cancellationToken);

                // Windows refuses to delete a folder that is not empty, which makes that the check.
                // In use or protected comes back the same way; either way it stays.
                ContainedDelete.Delete(child.FullName, resolvedRoot, directory: true);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // The root itself is unreadable; nothing to prune.
        }
    }

    private static bool IsLink(FileSystemInfo entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
}
