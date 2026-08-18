namespace SysTuneX.Core.Models;

/// <summary>
/// The result of a single system change. The old code swallowed every exception and
/// returned <c>false</c>, so the UI had no way to tell "nothing to do" from "access denied".
/// Every service now returns one of these instead.
/// </summary>
public readonly record struct OperationResult
{
    private OperationResult(bool success, bool changed, string? message, Exception? exception)
    {
        Success = success;
        Changed = changed;
        Message = message;
        Exception = exception;
    }

    /// <summary>The operation completed without an error.</summary>
    public bool Success { get; }

    /// <summary>The operation actually modified something (false when the system was already in the wanted state).</summary>
    public bool Changed { get; }

    /// <summary>Human readable detail, populated for failures and for skipped operations.</summary>
    public string? Message { get; }

    public Exception? Exception { get; }

    public static OperationResult Ok(string? message = null) => new(true, true, message, null);

    /// <summary>Succeeded, but the system was already in the requested state.</summary>
    public static OperationResult NoChange(string? message = null) => new(true, false, message, null);

    public static OperationResult Fail(string message, Exception? exception = null) =>
        new(false, false, message, exception);

    public override string ToString() => Success ? (Changed ? "OK" : "no change") : $"FAIL: {Message}";
}
