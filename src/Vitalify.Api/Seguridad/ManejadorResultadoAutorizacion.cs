using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Vitalify.Api.Configuracion;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Api.Seguridad;

/// <summary>
/// Cuando un usuario autenticado no tiene el rol requerido: registra <c>AccesoDenegado</c> en la auditoría
/// (usuario, ruta e IP) y responde 403 como ProblemDetails. Los demás casos siguen el flujo normal.
/// </summary>
internal sealed class ManejadorResultadoAutorizacion(IReloj reloj, ILogger<ManejadorResultadoAutorizacion> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _porDefecto = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await _porDefecto.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        var usuarioId = context.User.IdUsuario();
        var ruta = $"{context.Request.Method} {context.Request.Path}";
        var servicios = context.RequestServices;

        servicios.GetRequiredService<IRepositorioAuditoria>().Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.AccesoDenegado, reloj.AhoraUtc, usuarioId, context.Ip(),
            $"{ruta} con rol {context.User.Rol() ?? "desconocido"}."));
        await servicios.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync(context.RequestAborted);

        logger.LogWarning("Acceso denegado a {Ruta} para el usuario {UsuarioId}", ruta, usuarioId);

        await RespuestasProblema.EscribirAsync(
            context, StatusCodes.Status403Forbidden, "acceso-denegado", "Acceso denegado",
            "Tu rol no tiene permiso para acceder a este recurso.");
    }
}
