using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Seguridad;

/// <summary>
/// Access token: JWT HS256 con <c>sub</c>, <c>email</c>, <c>name</c>, <c>role</c> y <c>jti</c>.
/// Refresh token: 64 bytes aleatorios en Base64Url; en la base solo se guarda su SHA-256.
/// </summary>
internal sealed class GeneradorTokens(OpcionesJwt opciones) : IGeneradorTokens
{
    private const int BytesRefreshToken = 64;

    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credenciales = new(opciones.ClaveDeFirma(), SecurityAlgorithms.HmacSha256);

    public TokenDeAcceso GenerarAccessToken(Usuario usuario, DateTime ahora)
    {
        // El JWT trabaja en segundos: se trunca para que ExpiraEn coincida exactamente con el claim exp.
        var emitido = new DateTime(ahora.Ticks - (ahora.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        var expira = emitido.AddMinutes(opciones.MinutosAccessToken);
        var jti = Guid.NewGuid().ToString("N");

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = opciones.Emisor,
            Audience = opciones.Audiencia,
            IssuedAt = emitido,
            NotBefore = emitido,
            Expires = expira,
            SigningCredentials = _credenciales,
            Claims = new Dictionary<string, object>
            {
                [ClaimsVitalify.Sub] = usuario.Id.ToString(),
                [ClaimsVitalify.Email] = usuario.Correo.Valor,
                [ClaimsVitalify.Nombre] = usuario.Nombre,
                [ClaimsVitalify.Rol] = usuario.Rol.ToString(),
                [ClaimsVitalify.Jti] = jti,
            },
        });

        return new TokenDeAcceso(token, jti, expira);
    }

    public string GenerarRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(BytesRefreshToken));

    public string CalcularHash(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
}
