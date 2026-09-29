using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Puertos;

public sealed record TokenDeAcceso(string Token, string Jti, DateTime ExpiraEn);

public interface IGeneradorTokens
{
    TokenDeAcceso GenerarAccessToken(Usuario usuario, DateTime ahora);

    /// <summary>Valor aleatorio que se entrega al cliente. Nunca se guarda tal cual.</summary>
    string GenerarRefreshToken();

    /// <summary>Hash con el que se guarda y se busca el refresh token.</summary>
    string CalcularHash(string refreshToken);
}
