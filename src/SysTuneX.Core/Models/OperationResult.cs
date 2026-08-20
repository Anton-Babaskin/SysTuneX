namespace SysTuneX.Core.Models;

/// <summary>
/// The result of a single system change. The old code swallowed every exception and
/// returned <c>false</c>, so the UI had no way to tell "nothing to do" from "access denied".
/// Every service now returns one of these instead.
/// </summary>
public readonly record struct OperationResult
{
    private OperationResult(
        bool success,
        bool changed,
        string? message,
        Exception? exception,
        string? code = null,
        object?[]? args = null)
    {
        Success = success;
        Changed = changed;
        Message = message;
        Exception = exception;
        Code = code;
        Args = args ?? [];
    }

    /// <summary>The operation completed without an error.</summary>
    public bool Success { get; }

    /// <summary>The operation actually modified something (false when the system was already in the wanted state).</summary>
    public bool Changed { get; }

    /// <summary>
    /// Human readable detail in English, populated for failures and for skipped operations.
    /// Always present, and always English: this is what goes in the log, where one language the
    /// developer reads beats whichever language the machine happens to be set to.
    /// </summary>
    public string? Message { get; }

    public Exception? Exception { get; }

    /// <summary>
    /// Stable identifier of the message, when it came from <see cref="CoreMessages"/>. The UI
    /// translates it; anything without a code falls back to <see cref="Message"/>.
    /// </summary>
    public string? Code { get; }

    /// <summary>Values to substitute into the translated text, in the template's order.</summary>
    public IReadOnlyList<object?> Args { get; }

    public static OperationResult Ok(string? message = null) => new(true, true, message, null);

    /// <summary>Succeeded, but the system was already in the requested state.</summary>
    public static OperationResult NoChange(string? message = null) => new(true, false, message, null);

    public static OperationResult Fail(string message, Exception? exception = null) =>
        new(false, false, message, exception);

    /// <summary>Failure carrying a translatable message.</summary>
    public static OperationResult Fail(MessageTemplate template, params object?[] args) =>
        new(false, false, template.Render(args), null, template.Code, args);

    /// <summary>Failure carrying a translatable message and the exception behind it.</summary>
    public static OperationResult Fail(MessageTemplate template, Exception? exception, params object?[] args) =>
        new(false, false, template.Render(args), exception, template.Code, args);

    /// <summary>Nothing to do, with a translatable explanation of why.</summary>
    public static OperationResult NoChange(MessageTemplate template, params object?[] args) =>
        new(true, false, template.Render(args), null, template.Code, args);

    public override string ToString() => Success ? (Changed ? "OK" : "no change") : $"FAIL: {Message}";
}
