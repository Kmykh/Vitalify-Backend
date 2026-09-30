namespace Vitalify.Application.Comun;

public enum TipoError
{
    Validacion,
    Conflicto,
    NoAutorizado,
    NoEncontrado,
}

/// <summary>
/// Error esperado de un caso de uso. <see cref="Codigo"/> es estable y el cliente puede usarlo
/// (la API lo expone como <c>type</c> del ProblemDetails).
/// </summary>
public sealed record Error(TipoError Tipo, string Codigo, string Mensaje)
{
    /// <summary>Errores por campo, solo para <see cref="TipoError.Validacion"/>.</summary>
    public IReadOnlyDictionary<string, string[]> Detalles { get; init; } = new Dictionary<string, string[]>();

    public static Error Validacion(IReadOnlyDictionary<string, string[]> detalles) =>
        new(TipoError.Validacion, "validacion", "Los datos enviados no son válidos.") { Detalles = detalles };

    public static Error Conflicto(string codigo, string mensaje) => new(TipoError.Conflicto, codigo, mensaje);

    public static Error NoAutorizado(string codigo, string mensaje) => new(TipoError.NoAutorizado, codigo, mensaje);

    public static Error NoEncontrado(string codigo, string mensaje) => new(TipoError.NoEncontrado, codigo, mensaje);
}

/// <summary>Valor vacío para los casos de uso que no devuelven nada.</summary>
public readonly record struct Unidad
{
    public static readonly Unidad Valor;
}

/// <summary>
/// Resultado de un caso de uso: un valor o un <see cref="Error"/> tipado. Los errores esperados
/// (validación, conflicto, credenciales...) viajan aquí en lugar de lanzarse como excepciones.
/// </summary>
public sealed class Resultado<T>
{
    private readonly T? _valor;
    private readonly Error? _error;

    private Resultado(T valor)
    {
        _valor = valor;
        EsExito = true;
    }

    private Resultado(Error error) => _error = error;

    public bool EsExito { get; }

    public T Valor => EsExito ? _valor! : throw new InvalidOperationException("Un resultado fallido no tiene valor.");

    public Error Error => EsExito ? throw new InvalidOperationException("Un resultado exitoso no tiene error.") : _error!;

    public static Resultado<T> Exito(T valor) => new(valor);

    public static Resultado<T> Fallo(Error error) => new(error);

    public static implicit operator Resultado<T>(T valor) => Exito(valor);

    public static implicit operator Resultado<T>(Error error) => Fallo(error);
}
