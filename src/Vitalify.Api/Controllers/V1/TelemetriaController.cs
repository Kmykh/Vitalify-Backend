using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Telemetria;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Datos técnicos de la telemetría (administrador). Nunca incluye datos del paciente.</summary>
[ApiController]
[Route("api/v1/telemetria")]
[Authorize(Policy = Politicas.SoloAdministrador)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class TelemetriaController : ControllerBase
{
    /// <summary>Log de incidencias de telemetría, de la más reciente a la más antigua.</summary>
    /// <param name="listar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="desde">Desde esta fecha (UTC), inclusive.</param>
    /// <param name="hasta">Hasta esta fecha (UTC), inclusive.</param>
    /// <param name="dispositivo">Código del dispositivo.</param>
    /// <param name="tipo"><c>FueraDeRango</c>, <c>PresionIncompleta</c>, <c>DispositivoDesconocido</c>, <c>DispositivoSinVincular</c> o <c>MarcaDeTiempoInvalida</c>.</param>
    /// <param name="pagina">Número de página, desde 1.</param>
    /// <param name="tamano">Elementos por página, de 1 a 100.</param>
    /// <response code="200">Página de incidencias.</response>
    /// <response code="400">Filtros inválidos.</response>
    [HttpGet("incidencias")]
    [ProducesResponseType<PaginaRespuesta<IncidenciaDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginaRespuesta<IncidenciaDto>>> Incidencias(
        [FromServices] ListarIncidencias listar, CancellationToken ct,
        [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null, [FromQuery] string? dispositivo = null,
        [FromQuery] string? tipo = null, [FromQuery] int pagina = 1, [FromQuery] int tamano = 20)
    {
        var resultado = await listar.EjecutarAsync(new ListarIncidenciasConsulta(desde, hasta, dispositivo, tipo, pagina, tamano), ct);
        return resultado.EsExito ? Ok(PaginaRespuesta<IncidenciaDto>.Desde(resultado.Valor)) : this.Problema(resultado.Error);
    }
}
