namespace PRViewer.App.Audio;

// Motor de audio PR-Opus (Claudio, D:\PROYECTOS\PR-Opus), integrado a PR-Viewer
// con adaptaciones: entrada por Stream en memoria (invariante de solo lectura)
// y cabeceras waveOut en memoria nativa. Patrón Result original del motor.

/// <summary>Resultado de operación sin excepciones: éxito o error con mensaje.</summary>
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string Error { get; }

    protected Result(bool isSuccess, string error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, string.Empty);
    public static Result Failure(string error) => new(false, error);

    public static Result Try(Action action, string? errorMessagePrefix = null)
    {
        try
        {
            action();
            return Success();
        }
        catch (Exception ex)
        {
            var prefix = errorMessagePrefix is not null ? $"{errorMessagePrefix}: " : string.Empty;
            return Failure($"{prefix}{ex.Message}");
        }
    }
}

/// <summary>Resultado con valor.</summary>
public sealed class Result<T> : Result
{
    private readonly T _value;

    private Result(bool isSuccess, T value, string error) : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess ? _value : default!;

    public static Result<T> Success(T value) => new(true, value, string.Empty);
    public static new Result<T> Failure(string error) => new(false, default!, error);

    public static Result<T> Try(Func<T> func, string? errorMessagePrefix = null)
    {
        try
        {
            return Success(func());
        }
        catch (Exception ex)
        {
            var prefix = errorMessagePrefix is not null ? $"{errorMessagePrefix}: " : string.Empty;
            return Failure($"{prefix}{ex.Message}");
        }
    }
}
