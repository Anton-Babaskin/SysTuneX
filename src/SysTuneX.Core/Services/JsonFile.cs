using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Services;

/// <summary>
/// Reads and writes the small JSON files SysTuneX keeps its state in, so that a crash or a power
/// cut cannot leave one of them half-written.
///
/// Every store used to serialise straight into its file through <c>File.Create</c>, which truncates
/// first. For the length of the write the only copy on disk was a partial one, and an app killed or
/// a machine losing power in that window left a file that no longer parsed. For gamemode.json that
/// is the outcome game mode exists to prevent: the list of services it stopped is gone, the next
/// launch reads game mode as off, and nothing starts them again. For the others the user's list or
/// settings went quietly back to defaults, and the next save wrote over what was left.
///
/// <c>BackupService</c> already wrote the journal through a temp file and a rename, and set a corrupt
/// one aside; this is that approach in one place, so the next store gets it by default instead of by
/// remembering to copy it. It adds one thing the journal does not have, a flush to disk before the
/// rename, which is what makes it hold through a power cut and not only through a crash. The journal
/// keeps its own copy for now: moving it here would add that flush to every journal write, which is a
/// change to the most important file SysTuneX keeps and deserves its own review.
/// </summary>
public static class JsonFile
{
    /// <summary>
    /// Replaces <paramref name="path"/> with <paramref name="value"/> serialised as JSON, all or
    /// nothing: whoever reads the file afterwards finds the previous version or the new one, never a
    /// truncated mix of the two.
    /// </summary>
    /// <remarks>
    /// Throws on failure, like the <see cref="File"/> methods it is built from, and leaves the
    /// previous file as it was. Each store already decides what a failed save is worth - a warning,
    /// mostly, because the state is still right in memory - and that decision stays with it.
    /// </remarks>
    public static async Task WriteAsync<T>(
        string path,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        string tempPath = PrepareTempFile(path);
        try
        {
            await using (FileStream stream = CreateTempFile(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken).ConfigureAwait(false);

                // To the disk, not just to Windows' cache. NTFS journals the rename but not the
                // data, so without this a power cut just after the move can leave the real name on
                // a file of zeros - with the previous version already replaced.
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <inheritdoc cref="WriteAsync{T}"/>
    /// <remarks>
    /// For the one store whose interface is synchronous. Same guarantee, same exceptions.
    /// </remarks>
    public static void Write<T>(string path, T value, JsonSerializerOptions options)
    {
        string tempPath = PrepareTempFile(path);
        try
        {
            using (FileStream stream = CreateTempFile(tempPath))
            {
                JsonSerializer.Serialize(stream, value, options);
                stream.Flush(flushToDisk: true); // see WriteAsync
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Reads <paramref name="path"/> as JSON. A missing or unreadable file is reported in the result
    /// rather than thrown: none of these files is worth refusing to start over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A file that is there but cannot be read is logged as an error and moved aside as
    /// <c>&lt;name&gt;.corrupt-&lt;timestamp&gt;</c>, the way <c>BackupService</c> treats the journal.
    /// Carrying on with defaults is the right call for the app; leaving the file where the next
    /// save will overwrite it destroys the only copy of what was in it, and for gamemode.json that
    /// copy is the list of services still waiting to be started.
    /// </para>
    /// <para>
    /// A cancelled read is not a corrupt file. It is left where it is and the cancellation is
    /// thrown, so closing a page halfway through a load cannot cost the user their session.
    /// </para>
    /// </remarks>
    public static async Task<JsonFileRead<T>> ReadAsync<T>(
        string path,
        JsonSerializerOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // Checked first because the common case on most launches is "no file" - game mode off,
        // nothing saved yet - and that should not cost an exception each time a page loads.
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using FileStream stream = File.OpenRead(path);
            T? value = await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken).ConfigureAwait(false);
            return new JsonFileRead<T>(JsonFileStatus.Read, value);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Deleted between the check and the open, which is a missing file, not a broken one.
            return default;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return SetAside<T>(path, ex, logger);
        }
    }

    /// <summary>
    /// <see cref="ReadAsync{T}"/> for the store that reads in its constructor: a missing file is
    /// reported, an unreadable one is logged and moved aside, and neither throws.
    /// </summary>
    public static JsonFileRead<T> Read<T>(string path, JsonSerializerOptions options, ILogger logger)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            T? value = JsonSerializer.Deserialize<T>(stream, options);
            return new JsonFileRead<T>(JsonFileStatus.Read, value);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return default;
        }
        catch (Exception ex)
        {
            return SetAside<T>(path, ex, logger);
        }
    }

    /// <summary>
    /// The temp file sits next to the target, because only a rename within one volume replaces a
    /// file in a single step; a temp file anywhere else would make the move a copy.
    /// </summary>
    private static string PrepareTempFile(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
        }

        // A leftover temp file is deleted rather than reused: opening an existing file keeps its
        // owner and permissions, and the move would hand them to the real one.
        string tempPath = path + ".tmp";
        File.Delete(tempPath);
        return tempPath;
    }

    private static FileStream CreateTempFile(string tempPath) =>
        new(tempPath, FileMode.CreateNew, FileAccess.Write);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // The next write deletes it before starting, and nothing reads a .tmp file.
        }
    }

    private static JsonFileRead<T> SetAside<T>(string path, Exception error, ILogger logger)
    {
        string corruptPath = $"{path}.corrupt-{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";

        try
        {
            File.Move(path, corruptPath, overwrite: true);
        }
        catch (Exception moveError)
        {
            logger.LogError(
                error,
                "{Path} could not be read and could not be moved aside ({Reason}); using defaults, and the next save will replace it",
                path,
                moveError.Message);
            return new JsonFileRead<T>(JsonFileStatus.Unreadable, default);
        }

        logger.LogError(error, "{Path} could not be read; moved aside to {CorruptPath} and using defaults", path, corruptPath);
        return new JsonFileRead<T>(JsonFileStatus.Unreadable, default, corruptPath);
    }
}

/// <summary>What <see cref="JsonFile"/> found when it went to read a file.</summary>
public enum JsonFileStatus
{
    /// <summary>No file: a first run, or nothing has been saved yet.</summary>
    Missing,

    /// <summary>
    /// The file parsed. The value can still be null when the file holds JSON <c>null</c>, which is
    /// why this is a status and not a null check.
    /// </summary>
    Read,

    /// <summary>There was a file, it could not be read, and it has been moved aside.</summary>
    Unreadable,
}

/// <summary>
/// The outcome of a read. Stores mostly only want <see cref="Value"/>; the status is there for the
/// ones that treat "nothing on disk" differently from "something on disk that did not parse".
/// </summary>
/// <param name="Status">What was found.</param>
/// <param name="Value">The parsed value. Default unless <see cref="Status"/> is <see cref="JsonFileStatus.Read"/>.</param>
/// <param name="CorruptPath">Where an unreadable file was moved to; null when it was not moved.</param>
public readonly record struct JsonFileRead<T>(JsonFileStatus Status, T? Value, string? CorruptPath = null);
