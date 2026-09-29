using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Sesiones;

/// <summary>
/// Access token invalidado por logout antes de su expiración. Se identifica por su <c>jti</c> y deja de
/// importar cuando llega a <see cref="ExpiraEn"/>, porque a partir de ahí el token ya no es válido.
/// </summary>
public sealed class TokenRevocado
{
    public const int LargoMaximoJti = 64;

    public string Jti { get; private set; } = null!;
    public Guid UsuarioId { get; private set; }
    public DateTime ExpiraEn { get; private set; }

    private TokenRevocado()
    {
    }

    public static TokenRevocado Crear(string jti, Guid usuarioId, DateTime expiraEn)
    {
        if (string.IsNullOrWhiteSpace(jti) || jti.Length > LargoMaximoJti)
        {
            throw new ExcepcionDeDominio("El identificador del token (jti) no es válido.");
        }

        return new TokenRevocado { Jti = jti, UsuarioId = usuarioId, ExpiraEn = expiraEn };
    }
}
