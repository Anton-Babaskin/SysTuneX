using Xunit;
using Microsoft.Extensions.Logging;
using SysTuneX.Core.Diagnostics;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The log only earns its keep on a machine nobody can attach a debugger to, so the things
/// worth pinning are: it writes, it honours the level switch, and it can be read while open.
/// </summary>
public sealed class FileLoggerTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "systunex-logtests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writes_the_message_to_a_dated_file()
    {
        using var provider = new FileLoggerProvider(new LogLevelSwitch(), _directory);
        ILogger logger = provider.CreateLogger("SysTuneX.Core.Services.TweakEngine");

        logger.LogInformation("Applied tweak {Id}", "gpu_scheduling");

        string content = ReadAll(provider.CurrentFile);
        Assert.Contains("Applied tweak gpu_scheduling", content);
        Assert.Contains("INF", content);

        // The category is shortened, because the namespace prefix is the same on every line.
        Assert.Contains("TweakEngine:", content);
        Assert.DoesNotContain("SysTuneX.Core.Services.TweakEngine", content);
        Assert.Equal($"systunex-{DateTime.Now:yyyyMMdd}.log", Path.GetFileName(provider.CurrentFile));
    }

    [Fact]
    public void Debug_is_dropped_until_verbose_is_switched_on()
    {
        var level = new LogLevelSwitch();
        using var provider = new FileLoggerProvider(level, _directory);
        ILogger logger = provider.CreateLogger("Test");

        logger.LogDebug("quiet");
        Assert.DoesNotContain("quiet", ReadAll(provider.CurrentFile));

        level.IsVerbose = true;
        logger.LogDebug("loud");

        string content = ReadAll(provider.CurrentFile);
        Assert.Contains("loud", content);
        Assert.DoesNotContain("quiet", content);
    }

    [Fact]
    public void Exceptions_are_written_out_in_full()
    {
        using var provider = new FileLoggerProvider(new LogLevelSwitch(), _directory);
        ILogger logger = provider.CreateLogger("Test");

        logger.LogError(new UnauthorizedAccessException("Access to the registry key is denied."), "Write failed");

        string content = ReadAll(provider.CurrentFile);
        Assert.Contains("Write failed", content);
        Assert.Contains(nameof(UnauthorizedAccessException), content);
        Assert.Contains("Access to the registry key is denied.", content);
    }

    /// <summary>
    /// The diagnostics report reads the log while the logger still holds it open, and a tester
    /// will open it in an editor. Both need the handle to be shared.
    /// </summary>
    [Fact]
    public void The_file_can_be_read_while_the_logger_holds_it_open()
    {
        using var provider = new FileLoggerProvider(new LogLevelSwitch(), _directory);
        provider.CreateLogger("Test").LogInformation("still open");

        using var stream = new FileStream(provider.CurrentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        Assert.Contains("still open", reader.ReadToEnd());
    }

    [Fact]
    public void Logs_past_the_retention_window_are_deleted_on_start()
    {
        Directory.CreateDirectory(_directory);

        string stale = Path.Combine(_directory, "systunex-20200101.log");
        string recent = Path.Combine(_directory, "systunex-20991231.log");
        File.WriteAllText(stale, "old");
        File.WriteAllText(recent, "new");
        File.SetLastWriteTime(stale, DateTime.Now.AddDays(-30));
        File.SetLastWriteTime(recent, DateTime.Now);

        using var provider = new FileLoggerProvider(new LogLevelSwitch(), _directory, retainDays: 7);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void A_directory_that_cannot_be_written_does_not_throw()
    {
        // Logging must never be the thing that takes the app down, so a bad path is swallowed.
        using var provider = new FileLoggerProvider(new LogLevelSwitch(), "\0:\\nowhere");

        provider.CreateLogger("Test").LogInformation("dropped on the floor");
    }

    private static string ReadAll(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
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
}
