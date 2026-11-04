using System.Text.Json;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Services;
using SysTuneX.Core.Tests.Fakes;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// Every store's file goes through <see cref="JsonFile"/>, so its two promises are pinned here: a
/// write replaces the file whole or not at all, and a file that cannot be read is kept, not lost.
/// </summary>
public sealed class JsonFileTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "systunex-jsonfile-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "state.json");

    [Fact]
    public async Task A_write_leaves_the_file_and_nothing_else()
    {
        await JsonFile.WriteAsync(FilePath, new Sample { Name = "first" }, Json);
        await JsonFile.WriteAsync(FilePath, new Sample { Name = "second" }, Json);

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
        Assert.Equal("second", JsonSerializer.Deserialize<Sample>(await File.ReadAllTextAsync(FilePath))!.Name);
    }

    [Fact]
    public void A_synchronous_write_leaves_the_file_and_nothing_else()
    {
        JsonFile.Write(FilePath, new Sample { Name = "first" }, Json);

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    /// <summary>
    /// A temp file left by a crash is somebody else's file as far as the next write is concerned:
    /// it is replaced, not appended to or reused.
    /// </summary>
    [Fact]
    public async Task A_temp_file_left_by_a_crash_does_not_get_in_the_way()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath + ".tmp", "{ \"Name\": \"half a wri");

        await JsonFile.WriteAsync(FilePath, new Sample { Name = "whole" }, Json);

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
        Assert.Equal("whole", JsonSerializer.Deserialize<Sample>(await File.ReadAllTextAsync(FilePath))!.Name);
    }

    /// <summary>
    /// The case the whole type exists for. A write that dies part way through - here a value that
    /// throws while it is being serialised, standing in for a crash or a full disk - must leave
    /// the previous file exactly as it was. Writing in place truncates it first and fails this.
    /// </summary>
    [Fact]
    public async Task A_write_that_fails_part_way_leaves_the_previous_file_intact()
    {
        await JsonFile.WriteAsync(FilePath, new Sample { Name = "before" }, Json);
        string before = await File.ReadAllTextAsync(FilePath);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => JsonFile.WriteAsync(FilePath, new Exploding(), Json));

        Assert.Equal(before, await File.ReadAllTextAsync(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    [Fact]
    public void A_synchronous_write_that_fails_part_way_leaves_the_previous_file_intact()
    {
        JsonFile.Write(FilePath, new Sample { Name = "before" }, Json);
        string before = File.ReadAllText(FilePath);

        Assert.Throws<InvalidOperationException>(() => JsonFile.Write(FilePath, new Exploding(), Json));

        Assert.Equal(before, File.ReadAllText(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task A_file_written_here_reads_back()
    {
        await JsonFile.WriteAsync(FilePath, new Sample { Name = "round trip" }, Json);

        JsonFileRead<Sample> read = await JsonFile.ReadAsync<Sample>(FilePath, Json, new RecordingLogger<JsonFileTests>());

        Assert.Equal(JsonFileStatus.Read, read.Status);
        Assert.Equal("round trip", read.Value?.Name);
    }

    [Fact]
    public async Task A_missing_file_is_not_an_error()
    {
        var logger = new RecordingLogger<JsonFileTests>();

        JsonFileRead<Sample> read = await JsonFile.ReadAsync<Sample>(FilePath, Json, logger);

        Assert.Equal(JsonFileStatus.Missing, read.Status);
        Assert.Null(read.Value);
        Assert.Empty(logger.Entries);
        Assert.False(Directory.Exists(_directory));
    }

    /// <summary>
    /// Falling back to defaults is right; leaving the bad file where the next save overwrites it
    /// is not. It is moved aside with its contents untouched, and the move is logged as an error
    /// that names both paths.
    /// </summary>
    [Fact]
    public async Task A_corrupt_file_is_moved_aside_and_reported()
    {
        Directory.CreateDirectory(_directory);
        const string Truncated = "{ \"Name\": \"cut off mid";
        await File.WriteAllTextAsync(FilePath, Truncated);
        var logger = new RecordingLogger<JsonFileTests>();

        JsonFileRead<Sample> read = await JsonFile.ReadAsync<Sample>(FilePath, Json, logger);

        Assert.Equal(JsonFileStatus.Unreadable, read.Status);
        Assert.Null(read.Value);
        Assert.False(File.Exists(FilePath));

        string corrupt = Assert.Single(Directory.GetFiles(_directory, "state.json.corrupt-*"));
        Assert.Equal(corrupt, read.CorruptPath);
        Assert.Equal(Truncated, await File.ReadAllTextAsync(corrupt));

        LogEntry error = Assert.Single(logger.Errors);
        Assert.IsAssignableFrom<JsonException>(error.Exception);
        Assert.Contains(corrupt, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_corrupt_file_read_synchronously_is_moved_aside_and_reported()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "not json at all");
        var logger = new RecordingLogger<JsonFileTests>();

        JsonFileRead<Sample> read = JsonFile.Read<Sample>(FilePath, Json, logger);

        Assert.Equal(JsonFileStatus.Unreadable, read.Status);
        Assert.False(File.Exists(FilePath));
        Assert.Single(Directory.GetFiles(_directory, "state.json.corrupt-*"));
        Assert.Single(logger.Errors);
    }

    /// <summary>
    /// Closing a page halfway through a load cancels it. That says nothing about the file, so the
    /// file stays where it is - for gamemode.json, moving it would lose a live session.
    /// </summary>
    [Fact]
    public async Task A_cancelled_read_leaves_the_file_where_it_is()
    {
        await JsonFile.WriteAsync(FilePath, new Sample { Name = "still needed" }, Json);
        var logger = new RecordingLogger<JsonFileTests>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => JsonFile.ReadAsync<Sample>(FilePath, Json, logger, new CancellationToken(canceled: true)));

        Assert.Equal([FilePath], Directory.GetFiles(_directory));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // A leftover temp folder is not worth failing a test run over.
        }
    }

    private sealed class Sample
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Serialises its first property and throws on the second, so the failure lands after some of
    /// the file has already been produced - the shape of a crash, without needing one.
    /// </summary>
    private sealed class Exploding
    {
        public string Padding { get; } = new('x', 64 * 1024);

        public string Fails => throw new InvalidOperationException("Serialisation failed part way through");
    }
}
