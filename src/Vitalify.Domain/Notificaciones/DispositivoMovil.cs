using System.Security.Cryptography;
using System.Text;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Notificaciones;

public enum PlataformaMovil
{
    Android = 1,
    Ios = 2,
    Web = 3,
}

/// <summary>
/// Token de notificaciones push (FCM) de la app de un usuario. Se busca por su SHA-256; el token se guarda tal cual
/// porque hay que enviarlo a Firebase. Un mismo token pasa al último usuario que lo registra (teléfono compartido).
/// </summary>
public sealed class DispositivoMovil
{
    public const int LargoMaximoToken = 4096;

    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public string Token { get; private set; } = null!;
    public PlataformaMovil Plataforma { get; private set; }
    public DateTime RegistradoEn { get; private set; }
    public DateTime UltimoUsoEn { get; private set; }

    private DispositivoMovil()
    {
    }

    public static DispositivoMovil Registrar(Guid usuarioId, string token, PlataformaMovil plataforma, DateTime ahora)
    {
        var limpio = token?.Trim() ?? string.Empty;
        if (limpio.Length is 0 or > LargoMaximoToken || !Enum.IsDefined(plataforma))
        {
            throw new ExcepcionDeDominio("El token o la plataforma del dispositivo no son válidos.");
        }

        return new DispositivoMovil
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            TokenHash = Hash(limpio),
            Token = limpio,
            Plataforma = plataforma,
            RegistradoEn = ahora,
            UltimoUsoEn = ahora,
        };
    }

    /// <summary>El mismo token se vuelve a registrar (quizá con otra cuenta en el mismo teléfono).</summary>
    public void Renovar(Guid usuarioId, PlataformaMovil plataforma, DateTime ahora)
    {
        UsuarioId = usuarioId;
        Plataforma = plataforma;
        UltimoUsoEn = ahora;
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()))).ToLowerInvariant();
}
