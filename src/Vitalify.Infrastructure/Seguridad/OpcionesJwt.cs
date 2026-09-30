using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Vitalify.Infrastructure.Seguridad;

/// <summary>
/// Configuración de los tokens (sección <c>Jwt</c>, variables <c>Jwt__*</c> del .env). La usan el generador
/// de tokens y la validación de la API. Si la clave falta o es corta, la API no arranca.
/// </summary>
public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";
    public const int LargoMinimoClave = 32;

    public required string Emisor { get; init; }
    public required string Audiencia { get; init; }
    public required string Clave { get; init; }
    public required int MinutosAccessToken { get; init; }
    public required int MinutosInactividad { get; init; }
    public required int HorasMaximasSesion { get; init; }

    public SymmetricSecurityKey ClaveDeFirma() => new(Encoding.UTF8.GetBytes(Clave));

    public static OpcionesJwt Cargar(IConfiguration configuration)
    {
        var seccion = configuration.GetSection(Seccion);

        var clave = seccion["Clave"];
        if (string.IsNullOrWhiteSpace(clave) || clave.Length < LargoMinimoClave)
        {
            throw new InvalidOperationException(
                $"Jwt__Clave falta o tiene menos de {LargoMinimoClave} caracteres. " +
                "Genera una con `openssl rand -base64 48` y ponla en el archivo .env de la raíz del repositorio.");
        }

        return new OpcionesJwt
        {
            Emisor = Obligatorio(seccion, "Emisor"),
            Audiencia = Obligatorio(seccion, "Audiencia"),
            Clave = clave,
            MinutosAccessToken = Positivo(seccion, "MinutosAccessToken", 15),
            MinutosInactividad = Positivo(seccion, "MinutosInactividad", 30),
            HorasMaximasSesion = Positivo(seccion, "HorasMaximasSesion", 12),
        };
    }

    private static string Obligatorio(IConfigurationSection seccion, string clave) =>
        seccion[clave] is { Length: > 0 } valor && !string.IsNullOrWhiteSpace(valor)
            ? valor.Trim()
            : throw new InvalidOperationException($"Falta Jwt__{clave} en el archivo .env.");

    private static int Positivo(IConfigurationSection seccion, string clave, int porDefecto)
    {
        var texto = seccion[clave];
        if (string.IsNullOrWhiteSpace(texto))
        {
            return porDefecto;
        }

        return int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor) && valor > 0
            ? valor
            : throw new InvalidOperationException($"Jwt__{clave} debe ser un entero positivo.");
    }
}
