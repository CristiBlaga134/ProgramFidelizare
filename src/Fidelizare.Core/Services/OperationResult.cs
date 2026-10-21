namespace Fidelizare.Core.Services;

/// <summary>
/// Rezultatul unei operațiuni de business. Erorile previzibile — sold insuficient,
/// telefon deja folosit, bon deja înregistrat — sunt răspunsuri normale, nu excepții:
/// interfața le arată ca mesaj, nu ca eroare de program.
/// </summary>
public class OperationResult
{
    protected OperationResult(bool success, string? error)
    {
        Success = success;
        Error = error;
    }

    public bool Success { get; }

    public string? Error { get; }

    public bool Failed => !Success;

    public static OperationResult Ok() => new(true, null);

    public static OperationResult Fail(string error) => new(false, error);
}

/// <summary>Rezultat care poartă și o valoare atunci când operațiunea reușește.</summary>
public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(bool success, string? error, T? value) : base(success, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Ok(T value) => new(true, null, value);

    public static new OperationResult<T> Fail(string error) => new(false, error, default);
}
