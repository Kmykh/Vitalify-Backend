using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Notificaciones;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Tokens de notificaciones push (FCM) de la app móvil.</summary>
[ApiController]
[Route("api/v1/notificaciones")]
[Authorize(Policy = Politicas.PersonalClinico)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class NotificacionesController : ControllerBase
{
    /// <summary>Registra el token de FCM de este teléfono para recibir alertas con la app cerrada.</summary>
    /// <remarks>Llamarlo al iniciar sesión y cuando FCM renueve el token (<c>onTokenRefresh</c>).</remarks>
    /// <response code="204">Token registrado.</response>
    /// <response code="400">Falta el token o la plataforma no es Android, Ios ni Web.</response>
    [HttpPost("dispositivos")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Registrar(DispositivoMovilSolicitud solicitud, [FromServices] RegistrarDispositivoMovil registrar, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(new RegistrarDispositivoMovilComando(User.IdUsuario() ?? Guid.Empty, solicitud.Token, solicitud.Plataforma), ct);
        return resultado.EsExito ? NoContent() : this.Problema(resultado.Error);
    }

    /// <summary>Quita el token de este teléfono (al cerrar sesión).</summary>
    /// <response code="204">Token quitado (o no estaba registrado).</response>
    /// <response code="400">Falta el token.</response>
    [HttpDelete("dispositivos")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Quitar([FromBody] DispositivoMovilSolicitud solicitud, [FromServices] QuitarDispositivoMovil quitar, CancellationToken ct)
    {
        var resultado = await quitar.EjecutarAsync(User.IdUsuario() ?? Guid.Empty, solicitud.Token, ct);
        return resultado.EsExito ? NoContent() : this.Problema(resultado.Error);
    }
}
