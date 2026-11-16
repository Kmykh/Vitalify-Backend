using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Nnn;
using Vitalify.Application.Notas;

namespace Vitalify.Api.Controllers.V1;

/// <summary>
/// Automatización asistida del PAE (fase 9): "Mi turno", la confirmación de una sugerencia en un toque, la valoración NOC del
/// turno en lote y la nota de enfermería del turno. Vitalify precarga, agenda, recuerda y redacta; la enfermera decide y firma.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = Politicas.PersonalClinico)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class TurnoEnfermeriaController : ControllerBase
{
    /// <summary>Mi turno: los pacientes de mi servicio por riesgo, con lo vencido, lo próximo, las sugerencias y lo que falta valorar.</summary>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Pacientes de la guardia actual, del más grave al más estable.</response>
    /// <response code="403">La cuenta no tiene un servicio asignado (<c>usuario-sin-servicio</c>).</response>
    [HttpGet("mi-turno")]
    [ProducesResponseType<PanelTurnoDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PanelTurnoDto>> MiTurno([FromServices] ObtenerPanelTurno obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(new ObtenerPanelTurnoConsulta(User.IdUsuario() ?? Guid.Empty), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Confirma con un toque una sugerencia actual con su paquete (NIC con frecuencia por protocolo y NOC con basal y meta).</summary>
    /// <remarks>Queda como <c>confirmadoSinCambios</c>. Para cambiar algo, usa la confirmación completa.</remarks>
    /// <param name="id">Paciente.</param>
    /// <param name="solicitud">El diagnóstico sugerido.</param>
    /// <param name="confirmar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="201">Diagnóstico activo en el plan.</response>
    /// <response code="400">Código inválido.</response>
    /// <response code="403">Solo una enfermera confirma diagnósticos de enfermería.</response>
    /// <response code="404">El paciente no está hospitalizado o ese diagnóstico no está entre las sugerencias actuales (<c>sugerencia-no-encontrada</c>).</response>
    /// <response code="409">El diagnóstico ya está activo.</response>
    [HttpPost("pacientes/{id:guid}/plan-cuidados/sugerencias/confirmar-paquete")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<DiagnosticoPlanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticoPlanDto>> ConfirmarPaquete(
        Guid id, ConfirmarPaqueteSolicitud solicitud, [FromServices] ConfirmarPaqueteSugerido confirmar, CancellationToken ct)
    {
        var resultado = await confirmar.EjecutarAsync(
            new ConfirmarPaqueteComando(id, solicitud.DiagnosticoCodigo, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Created($"/api/v1/pacientes/{id}/plan-cuidados", resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>La valoración NOC del turno: cada resultado activo con la puntuación que propone el motor, para confirmar en lote.</summary>
    /// <param name="id">Paciente.</param>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Resultados con su propuesta (mediana de 30 min) y lo que cambió desde la última valoración.</response>
    /// <response code="404">El paciente no existe o no está hospitalizado.</response>
    [HttpGet("pacientes/{id:guid}/plan-cuidados/valoracion-turno")]
    [ProducesResponseType<ValoracionTurnoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ValoracionTurnoDto>> ValoracionTurno(Guid id, [FromServices] ObtenerValoracionTurno obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(new ObtenerValoracionTurnoConsulta(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Guarda la valoración NOC del turno de un paciente en una sola operación.</summary>
    /// <param name="id">Paciente.</param>
    /// <param name="solicitud">Cada resultado con sus indicadores valorados.</param>
    /// <param name="registrar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="201">Valoraciones guardadas.</response>
    /// <response code="400">Datos inválidos, o un resultado o indicador que no es de este paciente.</response>
    /// <response code="403">Solo una enfermera valora.</response>
    /// <response code="404">El paciente no existe o no está hospitalizado.</response>
    [HttpPost("pacientes/{id:guid}/plan-cuidados/valoracion-turno")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<ValoracionTurnoRegistradaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ValoracionTurnoRegistradaDto>> RegistrarValoracionTurno(
        Guid id, ValoracionTurnoSolicitud solicitud, [FromServices] RegistrarValoracionTurno registrar, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(new RegistrarValoracionTurnoComando(
            id,
            solicitud.Resultados?.Select(r => new ResultadoValoradoTurno(
                r?.ResultadoId, r?.Indicadores?.Select(i => new IndicadorValorado(i?.Codigo, i?.Puntuacion)).ToList())).ToList(),
            User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito
            ? Created($"/api/v1/pacientes/{id}/plan-cuidados", new ValoracionTurnoRegistradaDto(resultado.Valor))
            : this.Problema(resultado.Error);
    }

    /// <summary>Borrador de la nota de enfermería del turno en los cinco pasos del PAE, armado con los datos de la guardia actual.</summary>
    /// <remarks>No vale hasta que la enfermera lo revisa y lo firma. Leerlo cuenta como consulta del historial.</remarks>
    /// <param name="id">Paciente.</param>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Texto del borrador y, si ya se firmó la nota de esta guardia, su id.</response>
    /// <response code="404">El paciente no existe o no está hospitalizado.</response>
    [HttpGet("pacientes/{id:guid}/nota-turno/borrador")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<BorradorNotaTurnoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BorradorNotaTurnoDto>> BorradorNotaTurno(Guid id, [FromServices] ObtenerBorradorNotaTurno obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(new ObtenerBorradorNotaTurnoConsulta(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Firma la nota del PAE de la guardia actual (el borrador revisado). Una por paciente y guardia; se corrige 24 h.</summary>
    /// <param name="id">Paciente.</param>
    /// <param name="solicitud">El texto final.</param>
    /// <param name="firmar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="201">Nota registrada (tipo <c>ProcesoEnfermeria</c>).</response>
    /// <response code="400">Texto vacío o demasiado largo.</response>
    /// <response code="403">Solo una enfermera firma la nota del PAE.</response>
    /// <response code="404">El paciente no existe o no está hospitalizado.</response>
    /// <response code="409">Ya hay una nota del PAE en esta guardia (<c>nota-turno-ya-registrada</c>): se corrige.</response>
    [HttpPost("pacientes/{id:guid}/nota-turno")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<NotaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NotaDto>> FirmarNotaTurno(
        Guid id, FirmarNotaTurnoSolicitud solicitud, [FromServices] FirmarNotaTurno firmar, CancellationToken ct)
    {
        var resultado = await firmar.EjecutarAsync(new FirmarNotaTurnoComando(id, solicitud.Texto, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Created($"/api/v1/pacientes/{id}/notas", resultado.Valor) : this.Problema(resultado.Error);
    }
}
