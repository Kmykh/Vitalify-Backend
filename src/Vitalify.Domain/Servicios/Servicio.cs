using System.Text;
using System.Text.RegularExpressions;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Servicios;

/// <summary>
/// Servicio hospitalario (Emergencia, UVI, Medicina...). Agrupa camas, personal y la programación de turnos; las
/// alertas de un paciente van al personal del servicio de su cama.
/// </summary>
public sealed partial class Servicio
{
    public const int LargoMaximoCodigo = 30;
    public const int LargoMaximoNombre = 100;

    public Guid Id { get; private set; }

    /// <summary>Código único en mayúsculas, por ejemplo <c>EMERGENCIA</c> o <c>MEDICINA-B</c>.</summary>
    public string Codigo { get; private set; } = null!;

    public string Nombre { get; private set; } = null!;
    public bool Activo { get; private set; }

    private Servicio()
    {
    }

    public static Servicio Registrar(string codigo, string nombre)
    {
        var codigoNormalizado = NormalizarCodigo(codigo);
        if (!CodigoValido(codigoNormalizado))
        {
            throw new ExcepcionDeDominio("El código del servicio no es válido.");
        }

        var nombreLimpio = nombre?.Trim() ?? string.Empty;
        if (nombreLimpio.Length is 0 or > LargoMaximoNombre)
        {
            throw new ExcepcionDeDominio($"El nombre del servicio debe tener entre 1 y {LargoMaximoNombre} caracteres.");
        }

        return new Servicio { Id = Guid.NewGuid(), Codigo = codigoNormalizado, Nombre = nombreLimpio, Activo = true };
    }

    public static string NormalizarCodigo(string? codigo) => (codigo ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Letras, números y guiones, de 2 a 30 caracteres (después de normalizar).</summary>
    public static bool CodigoValido(string? codigo) => codigo is not null && Formato().IsMatch(NormalizarCodigo(codigo));

    /// <summary>
    /// Código a partir del nombre ("Medicina B" → <c>MEDICINA-B</c>): sin tildes, en mayúsculas y con guiones. Lo usa
    /// la migración de los servicios que ya estaban escritos en las camas (con la misma regla en SQL).
    /// </summary>
    public static string CodigoDesdeNombre(string nombre)
    {
        var texto = new StringBuilder();
        foreach (var c in (nombre ?? string.Empty).Trim().ToUpperInvariant())
        {
            var letra = c switch
            {
                'Á' => 'A', 'É' => 'E', 'Í' => 'I', 'Ó' => 'O', 'Ú' or 'Ü' => 'U', 'Ñ' => 'N',
                _ => c,
            };
            texto.Append(char.IsAsciiLetterOrDigit(letra) ? letra : '-');
        }

        var codigo = Guiones().Replace(texto.ToString(), "-").Trim('-');
        return codigo.Length > LargoMaximoCodigo ? codigo[..LargoMaximoCodigo].Trim('-') : codigo;
    }

    [GeneratedRegex("^[A-Z0-9-]{2,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex Formato();

    [GeneratedRegex("-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Guiones();
}
