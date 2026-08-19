using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Diagnostics;

/// <summary>
/// Writes the log to %ProgramData%\SysTuneX\logs.
///
/// Every service already logs through ILogger, but the app only ever registered the debug
/// provider — so on a user's machine all of it went nowhere and a misbehaving tweak left no
/// trace. This is the sink that makes those calls worth something.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLogWriter _writer;
    private readonly LogLevelSwitch _level;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    /// <param name="directory">Defaults to %ProgramData%\SysTuneX\logs; overridden by tests.</param>
    public FileLoggerProvider(LogLevelSwitch level, string? directory = null, int retainDays = 7)
    {
        _level = level;
        _writer = new FileLogWriter(directory ?? AppPaths.LogDirectory, retainDays);
    }

    /// <summary>The file currently being written, for the UI to point at.</summary>
    public string CurrentFile => _writer.CurrentFile;

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(Shorten(name), _writer, _level));

    public void Dispose() => _writer.Dispose();

    /// <summary>"SysTuneX.Core.Services.TweakEngine" reads better as "TweakEngine".</summary>
    private static string Shorten(string category)
    {
        int lastDot = category.LastIndexOf('.');
        return lastDot >= 0 && lastDot < category.Length - 1 ? category[(lastDot + 1)..] : category;
    }
}

internal sealed class FileLogger(string category, FileLogWriter writer, LogLevelSwitch level) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= level.Minimum;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(' ')
            .Append(Abbreviate(logLevel))
            .Append(' ')
            .Append(category)
            .Append(": ")
            .Append(formatter(state, exception));

        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        writer.Write(line.ToString());
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };
}

/// <summary>
/// Serialises writes and rolls the file at midnight.
///
/// The handle is opened with FileShare.ReadWrite so the log can be opened, copied or tailed
/// while SysTuneX is running - which is exactly when someone wants to read it.
/// </summary>
internal sealed class FileLogWriter : IDisposable
{
    private readonly Lock _gate = new();
    private readonly string _directory;
    private readonly int _retainDays;

    private StreamWriter? _stream;
    private DateTime _openedFor = DateTime.MinValue;
    private bool _disposed;

    public FileLogWriter(string directory, int retainDays)
    {
        _directory = directory;
        _retainDays = retainDays;
        CurrentFile = FileFor(DateTime.Now);
        Prune();
    }

    private string FileFor(DateTime date) => Path.Combine(_directory, $"systunex-{date:yyyyMMdd}.log");

    public string CurrentFile { get; private set; }

    public void Write(string line)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                StreamWriter stream = Open();
                stream.WriteLine(line);
            }
            catch
            {
                // A log that cannot be written must never take the app down with it.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _stream?.Dispose();
            _stream = null;
        }
    }

    private StreamWriter Open()
    {
        DateTime today = DateTime.Now.Date;
        if (_stream is not null && _openedFor == today)
        {
            return _stream;
        }

        _stream?.Dispose();

        Directory.CreateDirectory(_directory);
        CurrentFile = FileFor(today);

        var file = new FileStream(
            CurrentFile,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);

        _stream = new StreamWriter(file, Encoding.UTF8) { AutoFlush = true };
        _openedFor = today;
        return _stream;
    }

    /// <summary>Drops logs older than the retention window so the folder cannot grow forever.</summary>
    private void Prune()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            DateTime cutoff = DateTime.Now.Date.AddDays(-_retainDays);
            foreach (string file in Directory.EnumerateFiles(_directory, "systunex-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Housekeeping is not worth failing start-up over.
        }
    }
}
