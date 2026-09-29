using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Usuarios;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Application.Sesiones;

public sealed record RefrescarSesionComando(string RefreshToken, string? Ip);

public sealed class RefrescarSesionValidador : AbstractValidator<RefrescarSesionComando>
{
    public RefrescarSesionValidador()
    {
        RuleFor(c => c.RefreshToken).Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("El refresh token es obligatorio.");
    }
}

/// <summary>
/// HU04 E2: renueva el access token rotando el refresh token. Falla con <c>sesion-expirada</c> si la sesión
/// pasó el tiempo de inactividad o la duración máxima. Si se presenta un refresh token que ya se rotó
/// (posible robo), revoca todas las sesiones del usuario.
/// </summary>
public sealed class RefrescarSesion(
    IValidator<RefrescarSesionComando> validador,
    IRepositorioUsuarios usuarios,
    IRepositorioSesiones sesiones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IGeneradorTokens generador,
    IReloj reloj,
    OpcionesSesion opciones)
{
    public async Task<Resultado<SesionIniciada>> EjecutarAsync(RefrescarSesionComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var ahora = reloj.AhoraUtc;
        var sesion = await sesiones.ObtenerPorHashAsync(generador.CalcularHash(comando.RefreshToken), ct);
        if (sesion is null)
        {
            return ErroresSesion.TokenInvalido;
        }

        if (sesion.FueReemplazada)
        {
            var abiertas = await sesiones.ListarNoRevocadasAsync(sesion.UsuarioId, ct);
            foreach (var abierta in abiertas)
            {
                abierta.Revocar(ahora);
            }

            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.AccesoDenegado, ahora, sesion.UsuarioId, comando.Ip,
                $"Reuso de un refresh token ya rotado (sesión {sesion.Id}). Se revocaron {abiertas.Count} sesiones del usuario."));
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
            return ErroresSesion.TokenInvalido;
        }

        if (sesion.EstaRevocada)
        {
            return ErroresSesion.TokenInvalido;
        }

        if (sesion.Expiro(ahora, opciones.InactividadMaxima))
        {
            var motivo = ahora >= sesion.ExpiraEn ? "duración máxima de la sesión" : "inactividad";
            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.SesionExpirada, ahora, sesion.UsuarioId, comando.Ip,
                $"Sesión {sesion.Id} expirada por {motivo}."));
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
            return ErroresSesion.SesionExpirada;
        }

        var usuario = await usuarios.ObtenerPorIdAsync(sesion.UsuarioId, ct);
        if (usuario is not { Activo: true })
        {
            sesion.Revocar(ahora);
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
            return ErroresSesion.TokenInvalido;
        }

        var refreshToken = generador.GenerarRefreshToken();
        sesiones.Agregar(sesion.Rotar(generador.CalcularHash(refreshToken), ahora));
        var accessToken = generador.GenerarAccessToken(usuario, ahora);

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        return new SesionIniciada(accessToken.Token, accessToken.ExpiraEn, refreshToken, UsuarioDto.Desde(usuario));
    }
}
