using Microsoft.Extensions.Logging;

namespace SysTuneX.Core.Tests.Fakes;

/// <summary>
/// Keeps what was logged. "Fell back to defaults" and "fell back to defaults and said so" look the
/// same from outside, and only the second leaves anyone a way to find out what happened.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IReadOnlyList<LogEntry> Errors => [.. Entries.Where(e => e.Level == LogLevel.Error)];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }
}

public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
