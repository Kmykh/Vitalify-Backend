using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Auditoria;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Auditoría de seguridad para el administrador: solo acciones e ids, sin datos clínicos (Ley 29733).</summary>
[ApiController]
[Route("api/v1/auditoria")]
[Authorize(Policy = Politicas.SoloAdministrador)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class AuditoriaController : ControllerBase
{
    /// <summary>Registros de la auditoría, del más reciente al más antiguo.</summary>
    /// <remarks>
    /// Cada registro trae la acción, el usuario, la fecha, la IP y los ids que menciona (<c>referencias</c>). El texto
    /// del detalle no se devuelve: puede contener el motivo de un egreso o el correo de un login fallido.
    /// </remarks>
    /// <param name="listar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="accion">Por ejemplo, <c>ConsultaHistorialPaciente</c>, <c>AccesoDenegado</c> o <c>LoginFallido</c>.</param>
    /// <param name="usuarioId">Quién realizó la acción.</param>
    /// <param name="desde">Desde (UTC).</param>
    /// <param name="hasta">Hasta (UTC).</param>
    /// <param name="pagina">Desde 1.</param>
    /// <param name="tamano">De 1 a 100.</param>
    /// <response code="200">Página de registros.</response>
    /// <response code="400">Filtros inválidos.</response>
    [HttpGet]
    [ProducesResponseType<PaginaRespuesta<AuditoriaDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginaRespuesta<AuditoriaDto>>> Listar(
        [FromServices] ListarAuditoria listar, CancellationToken ct,
        [FromQuery] string? accion = null, [FromQuery] Guid? usuarioId = null, [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null, [FromQuery] int pagina = 1, [FromQuery] int tamano = 50)
    {
        var resultado = await listar.EjecutarAsync(new ListarAuditoriaConsulta(accion, usuarioId, desde, hasta, pagina, tamano), ct);
        return resultado.EsExito ? Ok(PaginaRespuesta<AuditoriaDto>.Desde(resultado.Valor)) : this.Problema(resultado.Error);
    }
}
