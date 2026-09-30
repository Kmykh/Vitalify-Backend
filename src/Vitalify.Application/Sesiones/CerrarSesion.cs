using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Sesiones;

namespace Vitalify.Application.Sesiones;

/// <param name="Jti">Identificador del access token con el que se hizo la petición.</param>
/// <param name="AccessTokenExpiraEn">Hasta cuándo hay que recordar ese <c>jti</c> como revocado.</param>
/// <param name="RefreshToken">Refresh token de esta sesión, si el cliente lo envía.</param>
public sealed record CerrarSesionComando(Guid UsuarioId, string Jti, DateTime AccessTokenExpiraEn, string? RefreshToken, string? Ip);

/// <summary>HU04 E1: invalida el access token actual y revoca el refresh token de la sesión.</summary>
public sealed class CerrarSesion(
    IRepositorioSesiones sesiones,
    IRepositorioTokensRevocados tokensRevocados,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IGeneradorTokens generador,
    IReloj reloj)
{
    public async Task<Resultado<Unidad>> EjecutarAsync(CerrarSesionComando comando, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comando.Jti))
        {
            return ErroresSesion.TokenInvalido;
        }

        var ahora = reloj.AhoraUtc;

        if (!string.IsNullOrWhiteSpace(comando.RefreshToken))
        {
            var sesion = await sesiones.ObtenerPorHashAsync(generador.CalcularHash(comando.RefreshToken), ct);

            // Solo se revocan sesiones propias: un refresh token ajeno se ignora.
            if (sesion is not null && sesion.UsuarioId == comando.UsuarioId)
            {
                sesion.Revocar(ahora);
            }
        }

        if (!await tokensRevocados.EstaRevocadoAsync(comando.Jti, ct))
        {
            tokensRevocados.Agregar(TokenRevocado.Crear(comando.Jti, comando.UsuarioId, comando.AccessTokenExpiraEn));
        }

        auditoria.Agregar(RegistroAuditoria.Registrar(AccionAuditoria.Logout, ahora, comando.UsuarioId, comando.Ip));

        return await unidadDeTrabajo.GuardarCambiosAsync(ct);
    }
}
