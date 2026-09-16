using System.Text.Json.Serialization;

namespace ComplianceCopilot.Shared.Results;

/// <summary>
/// Why a tool call didn't produce a value - kept as distinct, named cases rather than a
/// generic exception, so a caller (and a future test) can tell "doesn't exist" apart from
/// "exists but isn't yours" without parsing an exception message.
/// </summary>
public enum ToolErrorKind
{
    NotFound,
    Forbidden,
    ValidationFailed,
    Unavailable,
}

public sealed record ToolError(ToolErrorKind Kind, string Message);

/// <summary>
/// The result of an MCP tool call: either a value, or a typed error. Expected failure paths
/// (wrong owner, not found, bad input) return a <see cref="ToolOutcome{T}"/> - they are not
/// exceptions, since they are routine, anticipated outcomes of a real external API, not
/// exceptional conditions.
/// </summary>
public readonly struct ToolOutcome<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public ToolError? Error { get; }

    // Public constructor (System.Text.Json needs one to deserialize a struct with only
    // get-only properties) is still only meant to be reached via Success/Failure below -
    // the MCP client on the other side of the wire deserializes exactly this shape back.
    [JsonConstructor]
    public ToolOutcome(bool isSuccess, T? value, ToolError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static ToolOutcome<T> Success(T value) => new(true, value, null);

    public static ToolOutcome<T> Failure(ToolErrorKind kind, string message) =>
        new(false, default, new ToolError(kind, message));
}
