using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;
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

                                // Read-only leftovers are common in shader caches.
                                if (file.IsReadOnly)
                                {
                                    file.IsReadOnly = false;
                                }

                                file.Delete();
                                freed += size;
                                deleted++;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                // Locked by a running process: expected, not an error worth reporting.
                                skipped++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"{file.FullName}: {ex.Message}");
                            }
                        }

                        if (target.RemoveEmptyDirectories)
                        {
                            RemoveEmptyDirectories(path, cancellationToken);
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
                string expanded = Environment.ExpandEnvironmentVariables(raw);
                if (Directory.Exists(expanded))
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
    /// </summary>
    private static IEnumerable<FileInfo> EnumerateFiles(string root, string pattern, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string current = stack.Pop();

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (string subdirectory in subdirectories)
            {
                stack.Push(subdirectory);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(current, pattern);
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
            {
                FileInfo? info = null;
                try
                {
                    info = new FileInfo(file);
                    _ = info.Length;
                }
                catch
                {
                    info = null;
                }

                if (info is not null)
                {
                    yield return info;
                }
            }
        }
    }

    private static void RemoveEmptyDirectories(string root, CancellationToken cancellationToken)
    {
        try
        {
            foreach (string directory in Directory.GetDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemoveEmptyDirectories(directory, cancellationToken);

                try
                {
                    if (Directory.GetFileSystemEntries(directory).Length == 0)
                    {
                        Directory.Delete(directory);
                    }
                }
                catch
                {
                    // In use or protected; leave it.
                }
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

}
