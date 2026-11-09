using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Alertas;

namespace Vitalify.Api.Controllers.V1;

/// <summary>
/// Alertas clínicas (riesgo, caída, sin señal, toma pendiente y preocupación clínica) del servicio de quien consulta.
/// El administrador no las ve.
/// </summary>
[ApiController]
[Route("api/v1/alertas")]
[Authorize(Policy = Politicas.PersonalClinico)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class AlertasController : ControllerBase
{
    /// <summary>Alertas del servicio, por defecto las abiertas, ordenadas por prioridad y antigüedad.</summary>
    /// <param name="listar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="estado"><c>abiertas</c> (por defecto), <c>pendiente</c>, <c>en-atencion</c> o <c>atendida</c>.</param>
    /// <param name="servicioId">Servicio; solo el propio (por defecto, ese).</param>
    /// <param name="tipo"><c>riesgo</c>, <c>caida</c>, <c>sin-senal</c>, <c>toma-pendiente</c> o <c>preocupacion-clinica</c>.</param>
    /// <param name="desde">Creadas desde (UTC).</param>
    /// <param name="hasta">Creadas hasta (UTC).</param>
    /// <param name="pagina">Desde 1.</param>
    /// <param name="tamano">De 1 a 100.</param>
    /// <response code="200">Página de alertas.</response>
    /// <response code="400">Filtros inválidos.</response>
    /// <response code="403">Otro servicio, o la cuenta no tiene servicio asignado.</response>
    [HttpGet]
    [ProducesResponseType<PaginaRespuesta<AlertaDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginaRespuesta<AlertaDto>>> Listar(
        [FromServices] ListarAlertas listar, CancellationToken ct, [FromQuery] string? estado = null, [FromQuery] Guid? servicioId = null,
        [FromQuery] string? tipo = null, [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null,
        [FromQuery] int pagina = 1, [FromQuery] int tamano = 20)
    {
        var resultado = await listar.EjecutarAsync(
            new ListarAlertasConsulta(User.IdUsuario() ?? Guid.Empty, estado, servicioId, tipo, desde, hasta, pagina, tamano), ct);
        return resultado.EsExito ? Ok(PaginaRespuesta<AlertaDto>.Desde(resultado.Valor)) : this.Problema(resultado.Error);
    }

    /// <summary>Toma la alerta (pasa a en atención).</summary>
    /// <response code="200">Alerta en atención.</response>
    /// <response code="403">La alerta es de otro servicio.</response>
    /// <response code="404">La alerta no existe.</response>
    /// <response code="409">Ya fue atendida o la está atendiendo otra persona.</response>
    [HttpPost("{id:guid}/tomar")]
    [ProducesResponseType<AlertaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AlertaDto>> Tomar(Guid id, [FromServices] TomarAlerta tomar, CancellationToken ct)
    {
        var resultado = await tomar.EjecutarAsync(new TomarAlertaComando(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Atiende la alerta con una nota (qué se hizo).</summary>
    /// <remarks>
    /// Con <c>intervencionesEjecutadas</c> (ids de intervenciones del plan de cuidados), cada una queda registrada como
    /// ejecución con esta alerta, en la misma transacción. El médico solo puede registrar la NIC 7710.
    /// </remarks>
    /// <response code="200">Alerta atendida.</response>
    /// <response code="400">Falta la nota o no tiene entre 5 y 1000 caracteres, o una intervención no es del plan de esta hospitalización.</response>
    /// <response code="403">La alerta es de otro servicio, o un médico registró una intervención distinta de la NIC 7710.</response>
    /// <response code="404">La alerta no existe.</response>
    /// <response code="409">Ya fue atendida, o una intervención o su diagnóstico ya no están activos.</response>
    [HttpPost("{id:guid}/atender")]
    [ProducesResponseType<AlertaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AlertaDto>> Atender(Guid id, AtenderAlertaSolicitud solicitud, [FromServices] AtenderAlerta atender, CancellationToken ct)
    {
        var resultado = await atender.EjecutarAsync(
            new AtenderAlertaComando(id, solicitud.Nota, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip(), solicitud.IntervencionesEjecutadas), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Sugerencias NNN de la evaluación que abrió la alerta (fase 8).</summary>
    /// <remarks>
    /// Usa la evaluación del detalle de la alerta (riesgo) o la última hasta que se creó (caída), con la toma manual que contaba
    /// entonces. No repite los diagnósticos que ya están activos en el plan. Vitalify sugiere; la enfermera decide.
    /// </remarks>
    /// <response code="200">Sugerencias (puede estar vacía).</response>
    /// <response code="404">La alerta no existe.</response>
    [HttpGet("{id:guid}/sugerencias-nnn")]
    [ProducesResponseType<Application.Nnn.SugerenciasAlertaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Application.Nnn.SugerenciasAlertaDto>> SugerenciasNnn(
        Guid id, [FromServices] Application.Nnn.ObtenerSugerenciasAlerta obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(new Application.Nnn.ObtenerSugerenciasAlertaConsulta(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
