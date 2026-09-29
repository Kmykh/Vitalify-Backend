using System.Text.RegularExpressions;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Usuarios;

/// <summary>
/// Correo electrónico normalizado (sin espacios alrededor y en minúsculas), para que la comparación y el
/// índice único no dependan de cómo lo escribió el usuario.
/// </summary>
public sealed partial record Correo
{
    public const int LargoMaximo = 254;

    public string Valor { get; }

    private Correo(string valor) => Valor = valor;

    public static Correo Crear(string? valor) =>
        TryCrear(valor, out var correo) ? correo : throw new ExcepcionDeDominio("El correo no tiene un formato válido.");

    public static bool TryCrear(string? valor, out Correo correo)
    {
        var normalizado = Normalizar(valor);
        if (normalizado.Length is 0 or > LargoMaximo || !Formato().IsMatch(normalizado))
        {
            correo = null!;
            return false;
        }

        correo = new Correo(normalizado);
        return true;
    }

    public static bool EsValido(string? valor) => TryCrear(valor, out _);

    public override string ToString() => Valor;

    private static string Normalizar(string? valor) => (valor ?? string.Empty).Trim().ToLowerInvariant();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Formato();
}
