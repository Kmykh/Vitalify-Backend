using System.Security.Cryptography;
using System.Text;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Vinculacion;

/// <summary>
/// Código de un solo uso que el wearable muestra como QR para que la enfermera lo vincule a un paciente. Solo se
/// guarda su SHA-256: el código en claro viaja una vez por MQTT a la pantalla del dispositivo y nunca se escribe en la
/// base ni en el log. Al escanearlo se emite un token de vinculación (también guardado como hash) que permite
/// confirmar la vinculación durante unos minutos.
/// </summary>
/// <remarks>
/// Ciclo de vida:
/// <code>
/// Generar --(escaneo válido)--> con token --(confirmar)--> usado
///    |                             |
///    +--(vence, 5 intentos fallidos o se genera otro)--> invalidado
/// </code>
/// Un dispositivo tiene como máximo un código activo (ni usado ni invalidado): lo garantiza un índice único parcial.
/// </remarks>
public sealed class CodigoVinculacion
{
    /// <summary>Sin 0, 1, I, L ni O, para que no se confundan al leerlos en la pantalla.</summary>
    public const string Alfabeto = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public const int Largo = 8;

    /// <summary>SHA-256 en hexadecimal.</summary>
    public const int LargoHash = 64;

    public Guid Id { get; private set; }
    public Guid DispositivoId { get; private set; }
    public string HashCodigo { get; private set; } = null!;
    public DateTime CreadoEn { get; private set; }
    public DateTime ExpiraEn { get; private set; }
    public DateTime? UsadoEn { get; private set; }
    public DateTime? InvalidadoEn { get; private set; }
    public int IntentosFallidos { get; private set; }
    public string? TokenHash { get; private set; }
    public DateTime? TokenExpiraEn { get; private set; }

    private CodigoVinculacion()
    {
    }

    /// <summary>Ni usado ni invalidado (puede estar vencido: lo renueva el worker).</summary>
    public bool EstaActivo => UsadoEn is null && InvalidadoEn is null;

    public bool EstaVigente(DateTime ahora) => EstaActivo && ahora < ExpiraEn;

    /// <summary>Alguien escaneó el QR y todavía puede confirmar la vinculación.</summary>
    public bool TieneTokenVigente(DateTime ahora) => EstaActivo && TokenHash is not null && TokenExpiraEn > ahora;

    /// <summary>Genera un código nuevo. Devuelve también el código en claro, que solo debe ir al wearable.</summary>
    public static (CodigoVinculacion Codigo, string EnClaro) Generar(Guid dispositivoId, DateTime ahora, TimeSpan vigencia)
    {
        if (vigencia <= TimeSpan.Zero)
        {
            throw new ExcepcionDeDominio("La vigencia del código de vinculación debe ser positiva.");
        }

        var enClaro = RandomNumberGenerator.GetString(Alfabeto, Largo);
        var codigo = new CodigoVinculacion
        {
            Id = Guid.NewGuid(),
            DispositivoId = dispositivoId,
            HashCodigo = Hash(enClaro),
            CreadoEn = ahora,
            ExpiraEn = ahora + vigencia,
        };
        return (codigo, enClaro);
    }

    /// <summary>Compara el hash del código presentado con el guardado, en tiempo constante.</summary>
    public bool Coincide(string? codigo) => codigo is not null && IgualesEnTiempoConstante(HashCodigo, Hash(Normalizar(codigo)));

    /// <summary>Cuenta un código equivocado. Al llegar a <paramref name="maximo"/> invalida el código y devuelve true.</summary>
    public bool RegistrarIntentoFallido(DateTime ahora, int maximo)
    {
        ExigirActivo();
        IntentosFallidos++;
        if (IntentosFallidos < maximo)
        {
            return false;
        }

        InvalidadoEn = ahora;
        return true;
    }

    /// <summary>
    /// Emite el token de vinculación tras un escaneo válido. Si se vuelve a escanear el mismo QR, el token anterior
    /// deja de valer. Devuelve el token en claro, que solo recibe la app de la enfermera.
    /// </summary>
    public string EmitirToken(DateTime ahora, TimeSpan vigencia)
    {
        if (!EstaVigente(ahora))
        {
            throw new ExcepcionDeDominio("El código de vinculación venció o ya no está activo.");
        }

        if (vigencia <= TimeSpan.Zero)
        {
            throw new ExcepcionDeDominio("La vigencia del token de vinculación debe ser positiva.");
        }

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        TokenHash = Hash(token);
        TokenExpiraEn = ahora + vigencia;
        return token;
    }

    public bool TokenCoincide(string? token, DateTime ahora) =>
        token is not null && TieneTokenVigente(ahora) && IgualesEnTiempoConstante(TokenHash!, Hash(token.Trim()));

    /// <summary>La vinculación se confirmó: el código ya no sirve.</summary>
    public void MarcarUsado(DateTime ahora)
    {
        if (!TieneTokenVigente(ahora))
        {
            throw new ExcepcionDeDominio("La vinculación venció: hay que escanear el QR de nuevo.");
        }

        UsadoEn = ahora;
    }

    /// <summary>Lo reemplaza otro código, el sensor se vinculó por otra vía o se agotaron los intentos.</summary>
    public void Invalidar(DateTime ahora)
    {
        if (EstaActivo)
        {
            InvalidadoEn = ahora;
        }
    }

    /// <summary>SHA-256 en hexadecimal (minúsculas).</summary>
    public static string Hash(string valor) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();

    public static string Normalizar(string codigo) => codigo.Trim().ToUpperInvariant();

    /// <summary>8 caracteres del <see cref="Alfabeto"/> (después de normalizar a mayúsculas).</summary>
    public static bool FormatoValido(string? codigo)
    {
        if (codigo is null)
        {
            return false;
        }

        var normalizado = Normalizar(codigo);
        return normalizado.Length == Largo && normalizado.All(c => Alfabeto.Contains(c, StringComparison.Ordinal));
    }

    private static bool IgualesEnTiempoConstante(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void ExigirActivo()
    {
        if (!EstaActivo)
        {
            throw new ExcepcionDeDominio("El código de vinculación ya no está activo.");
        }
    }
}
