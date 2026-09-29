using System.Globalization;
using System.Security.Claims;
using Vitalify.Infrastructure.Seguridad;

namespace Vitalify.Api.Seguridad;

/// <summary>Lectura de los claims del access token.</summary>
internal static class UsuarioAutenticado
{
    public static Guid? IdUsuario(this ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(ClaimsVitalify.Sub), out var id) ? id : null;

    public static string? Rol(this ClaimsPrincipal usuario) => usuario.FindFirstValue(ClaimsVitalify.Rol);

    public static string? Jti(this ClaimsPrincipal usuario) => usuario.FindFirstValue(ClaimsVitalify.Jti);

    public static DateTime? ExpiraEn(this ClaimsPrincipal usuario) =>
        long.TryParse(usuario.FindFirstValue(ClaimsVitalify.Expiracion), NumberStyles.Integer, CultureInfo.InvariantCulture, out var segundos)
            ? DateTimeOffset.FromUnixTimeSeconds(segundos).UtcDateTime
            : null;

    public static string? Ip(this HttpContext context) => context.Connection.RemoteIpAddress?.ToString();
}
