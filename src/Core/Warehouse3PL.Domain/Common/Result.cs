namespace Warehouse3PL.Domain.Common;

/// <summary>
/// Functional result encapsulation for application and domain operations.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public string? ErrorCode { get; }

    protected Result(bool isSuccess, string? error = null, string? errorCode = null)
    {
        if (isSuccess && error != null)
            throw new InvalidOperationException("Success result cannot contain an error message.");
        if (!isSuccess && string.IsNullOrWhiteSpace(error))
            throw new InvalidOperationException("Failure result must contain an error message.");

        IsSuccess = isSuccess;
        Error = error;
        ErrorCode = errorCode;
    }

    public static Result Success() => new(true);
    public static Result Failure(string error, string? errorCode = null) => new(false, error, errorCode);
}

public class Result<T> : Result
{
    private readonly T? _value;

    protected Result(T? value, bool isSuccess, string? error = null, string? errorCode = null)
        : base(isSuccess, error, errorCode)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failure result.");

    public static Result<T> Success(T value) => new(value, true);
    public static new Result<T> Failure(string error, string? errorCode = null) => new(default, false, error, errorCode);
}

