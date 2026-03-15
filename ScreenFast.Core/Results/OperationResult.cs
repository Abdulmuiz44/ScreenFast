namespace ScreenFast.Core.Results;

public class OperationResult
{
    protected OperationResult(bool isSuccess, AppError? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public AppError? Error { get; }

    public static OperationResult Success() => new(true, null);

    public static OperationResult Failure(AppError error) => new(false, error);
}

public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool isSuccess, T? value, AppError? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Success(T value) => new(true, value, null);

    public static new OperationResult<T> Failure(AppError error) => new(false, default, error);
}
