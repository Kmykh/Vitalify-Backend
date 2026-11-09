using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Sistema;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Estado del sistema para el administrador. No expone datos de pacientes.</summary>
[ApiController]
[Route("api/v1/sistema")]
[Authorize(Policy = Politicas.SoloAdministrador)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class SistemaController : ControllerBase
{
    /// <summary>Cuánto ocupa la base y cada tabla, para vigilar el límite del plan gratuito de Supabase (500 MB).</summary>
    /// <remarks>
    /// <c>bytesBase</c> es toda la base (<c>pg_database_size</c>), que es lo que cuenta Supabase; cada tabla trae su tamaño
    /// con índices (<c>pg_total_relation_size</c>) y las filas aproximadas. Solo tamaños: ningún dato clínico.
    /// </remarks>
    /// <param name="consultar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Tamaños, de la tabla más grande a la más pequeña.</response>
    [HttpGet("almacenamiento")]
    [ProducesResponseType<AlmacenamientoDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AlmacenamientoDto>> Almacenamiento([FromServices] ConsultarAlmacenamiento consultar, CancellationToken ct) =>
        Ok(await consultar.EjecutarAsync(ct));

    /// <summary>Indicadores del proceso de enfermería NNN para el capítulo de validación de la tesis (fase 8).</summary>
    /// <remarks>
    /// Solo agregados, nunca datos de pacientes:
    /// <list type="bullet">
    /// <item>sugerencias emitidas, confirmadas, descartadas, expiradas y pendientes, con la tasa de aceptación por regla;</item>
    /// <item>tiempo desde la alerta hasta la primera intervención NIC registrada por ella (promedio y mediana, en minutos);</item>
    /// <item>porcentaje de diagnósticos con al menos una valoración NOC en cada guardia en que estuvieron activos (6 h o más);</item>
    /// <item>cambio medio de la puntuación NOC desde la basal hasta el cierre de los diagnósticos resueltos.</item>
    /// </list>
    /// </remarks>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="desde">Inicio (UTC); por defecto, 30 días antes de <c>hasta</c>.</param>
    /// <param name="hasta">Fin (UTC); por defecto, ahora. El rango es de hasta 366 días.</param>
    /// <response code="200">Indicadores del rango.</response>
    /// <response code="400">Rango inválido.</response>
    [HttpGet("indicadores-nnn")]
    [ProducesResponseType<Application.Nnn.IndicadoresNnnDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Application.Nnn.IndicadoresNnnDto>> IndicadoresNnn(
        [FromServices] Application.Nnn.ObtenerIndicadoresNnn obtener, CancellationToken ct, [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null)
    {
        var resultado = await obtener.EjecutarAsync(new Application.Nnn.ObtenerIndicadoresNnnConsulta(desde, hasta), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
