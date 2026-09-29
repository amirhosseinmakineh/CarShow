using System.Data.SqlTypes;

public record Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string Message { get; }
    public T Data { get; }

    private Result(T data, string message, bool isSuccess)
    {
        Data = data;
        Message = message;
        IsSuccess = isSuccess;
    }
    private Result(string message, bool isSuccess)
    {
        Message = message;
        IsSuccess = isSuccess;
    }

    public static Result<T> Success(T data, string message = "")
    {
        return new Result<T>(data, message, true);
    }
    public static Result<T> Success(string message = "")
    {
        return new Result<T>(message, true);
    }
    public static Result<T> Failure(string message)
    {
        return new Result<T>(default, message, false);
    }
}