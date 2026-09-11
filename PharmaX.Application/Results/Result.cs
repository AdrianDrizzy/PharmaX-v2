using System.Diagnostics.CodeAnalysis;

namespace PharmaX.Application.Results;

[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "Error is the name the whole solution uses for a failed "
                  + "result, in the code and in the specification. Renaming "
                  + "it to please a cross-language rule that no consumer of "
                  + "this desktop application will ever hit would cost "
                  + "clarity at every call site.")]
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new("", "");
}

public class Result
{
    protected Result(bool success, Error error)
    {
        IsSuccess = success;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Ok() => new(true, Error.None);
    public static Result Fail(string code, string message) =>
        new(false, new Error(code, message));
}

[SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
    Justification = "Result<T>.Ok and Result<T>.Fail are the intended way to "
                  + "construct one, and the type argument is inferred at "
                  + "every real call site. A separate non-generic factory "
                  + "would be more typing for the same result.")]
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, bool success, Error error)
        : base(success, error) => _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            "Cannot read the value of a failed result.");

    public static Result<T> Ok(T value) => new(value, true, Error.None);
    public static new Result<T> Fail(string code, string message) =>
        new(default, false, new Error(code, message));

    /// Carries a failure across a change of value type without restating
    /// its code and message at every call site.
    public static Result<T> From(Result failure) =>
        new(default, false, failure.Error);
}
