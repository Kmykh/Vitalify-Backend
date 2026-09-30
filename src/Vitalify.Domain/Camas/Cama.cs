using System.Text.RegularExpressions;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Camas;

/// <summary>
/// Cama del catálogo del hospital. No guarda si está ocupada: eso se deriva de las hospitalizaciones activas.
/// </summary>
public sealed partial class Cama
{
    public const int LargoMaximoCodigo = 20;
    public const int LargoMaximoServicio = 100;

    public Guid Id { get; private set; }

    /// <summary>Código único, normalizado en mayúsculas (por ejemplo <c>MED-B-05</c>).</summary>
    public string Codigo { get; private set; } = null!;

    public string Servicio { get; private set; } = null!;
    public bool Activa { get; private set; }

    private Cama()
    {
    }

    public static Cama Registrar(string codigo, string servicio)
    {
        var codigoNormalizado = NormalizarCodigo(codigo);
        if (!CodigoValido(codigoNormalizado))
        {
            throw new ExcepcionDeDominio("El código de la cama no es válido.");
        }

        var servicioLimpio = servicio?.Trim() ?? string.Empty;
        if (servicioLimpio.Length is 0 or > LargoMaximoServicio)
        {
            throw new ExcepcionDeDominio("El servicio de la cama es obligatorio.");
        }

        return new Cama { Id = Guid.NewGuid(), Codigo = codigoNormalizado, Servicio = servicioLimpio, Activa = true };
    }

    public static string NormalizarCodigo(string? codigo) => (codigo ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Letras, números y guiones, de 2 a 20 caracteres (después de normalizar).</summary>
    public static bool CodigoValido(string? codigo) => codigo is not null && Formato().IsMatch(NormalizarCodigo(codigo));

    public void Desactivar() => Activa = false;

    [GeneratedRegex("^[A-Z0-9-]{2,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex Formato();
}
