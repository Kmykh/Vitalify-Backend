using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Nnn;

namespace Vitalify.Api.Controllers.V1;

/// <summary>
/// Proceso de enfermería NNN (fase 8): sugerencias del motor, plan de cuidados, ejecución de intervenciones NIC y valoración
/// NOC. Vitalify sugiere; la enfermera decide. La enfermera crea y valora; el médico lee y solo puede agregar o registrar la NIC
/// 7710. No hay DELETE (NTS N° 139-MINSA/2018/DGAIN): un diagnóstico se resuelve o se descarta con motivo.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = Politicas.PersonalClinico)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class PlanCuidadosController : ControllerBase
{
    /// <summary>Sugerencias NNN actuales de la hospitalización activa, con su motivo y la versión de las reglas.</summary>
    /// <remarks>Se calculan con la última evaluación NEWS2/MEWS, las alertas abiertas y la toma manual vigente. Queda auditado.</remarks>
    /// <param name="id">Paciente.</param>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Sugerencias (puede estar vacía).</response>
    /// <response code="404">El paciente no existe o no está hospitalizado.</response>
    [HttpGet("pacientes/{id:guid}/nnn/sugerencias")]
    [ProducesResponseType<SugerenciasPacienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SugerenciasPacienteDto>> Sugerencias(Guid id, [FromServices] ObtenerSugerenciasPaciente obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(new ObtenerSugerenciasConsulta(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Plan de cuidados completo de un episodio: diagnósticos, intervenciones con sus ejecuciones, resultados y la serie de valoraciones NOC.</summary>
    /// <param name="id">Paciente.</param>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="hospitalizacionId">Episodio; por defecto el activo o el último.</param>
    /// <response code="200">Plan del episodio (también los diagnósticos resueltos y descartados).</response>
    /// <response code="404">El paciente no existe, nunca estuvo hospitalizado o el episodio no es suyo.</response>
    [HttpGet("pacientes/{id:guid}/plan-cuidados")]
    [ProducesResponseType<PlanCuidadosDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlanCuidadosDto>> Plan(
        Guid id, [FromServices] ObtenerPlanCuidados obtener, CancellationToken ct, [FromQuery] Guid? hospitalizacionId = null)
    {
        var resultado = await obtener.EjecutarAsync(new ObtenerPlanCuidadosConsulta(id, hospitalizacionId, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Confirma un diagnóstico NANDA-I (sugerido o del catálogo) con sus intervenciones NIC y resultados NOC.</summary>
    /// <param name="id">Paciente.</param>
    /// <param name="solicitud">Diagnóstico, origen y lo planificado.</param>
    /// <param name="confirmar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="201">Diagnóstico activo en el plan.</response>
    /// <response code="400">Datos inválidos o códigos que no están en el catálogo.</response>
    /// <response code="403">Solo una enfermera confirma diagnósticos de enfermería.</response>
    /// <response code="404">El paciente no existe o no está hospitalizado.</response>
    /// <response code="409">El diagnóstico ya está activo (<c>diagnostico-ya-activo</c>).</response>
    [HttpPost("pacientes/{id:guid}/plan-cuidados/diagnosticos")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<DiagnosticoPlanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticoPlanDto>> Confirmar(
        Guid id, ConfirmarDiagnosticoSolicitud solicitud, [FromServices] ConfirmarDiagnostico confirmar, CancellationToken ct)
    {
        var resultado = await confirmar.EjecutarAsync(new ConfirmarDiagnosticoComando(
            id, solicitud.DiagnosticoCodigo, solicitud.Origen, solicitud.ReglaOrigen, solicitud.AlertaId,
            solicitud.Intervenciones?.Select(i => new IntervencionSolicitada(i?.Codigo, i?.Frecuencia)).ToList(),
            solicitud.Resultados?.Select(r => new ResultadoSolicitado(r?.Codigo, r?.PuntuacionBasal, r?.PuntuacionMeta)).ToList(),
            User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Created($"/api/v1/pacientes/{id}/plan-cuidados", resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Descarta una sugerencia del motor con motivo. Queda registrada para medir la tasa de aceptación.</summary>
    /// <param name="id">Paciente.</param>
    /// <param name="solicitud">Diagnóstico, regla y motivo.</param>
    /// <param name="descartar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="201">Sugerencia descartada (diagnóstico en estado Descartado).</response>
    /// <response code="400">Falta el motivo (5 a 300 caracteres) o la regla.</response>
    /// <response code="404">El paciente no existe o no está hospitalizado.</response>
    /// <response code="409">Ese diagnóstico ya está activo en el plan.</response>
    [HttpPost("pacientes/{id:guid}/plan-cuidados/sugerencias/descartar")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<DiagnosticoPlanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticoPlanDto>> Descartar(
        Guid id, DescartarSugerenciaSolicitud solicitud, [FromServices] DescartarSugerencia descartar, CancellationToken ct)
    {
        var resultado = await descartar.EjecutarAsync(new DescartarSugerenciaComando(
            id, solicitud.DiagnosticoCodigo, solicitud.ReglaOrigen, solicitud.Motivo, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Created($"/api/v1/pacientes/{id}/plan-cuidados", resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Resuelve o descarta un diagnóstico activo, con motivo. No se puede volver a Activo: si reaparece, se registra uno nuevo.</summary>
    /// <param name="id">Diagnóstico del plan.</param>
    /// <param name="solicitud">Estado y motivo.</param>
    /// <param name="cambiar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Diagnóstico cerrado.</response>
    /// <response code="400">Estado distinto de Resuelto o Descartado, o falta el motivo.</response>
    /// <response code="404">El diagnóstico no existe.</response>
    /// <response code="409">Ya estaba cerrado (<c>diagnostico-cerrado</c>).</response>
    [HttpPut("plan-cuidados/diagnosticos/{id:guid}/estado")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<DiagnosticoPlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DiagnosticoPlanDto>> CambiarEstado(
        Guid id, CambiarEstadoDiagnosticoSolicitud solicitud, [FromServices] CambiarEstadoDiagnostico cambiar, CancellationToken ct)
    {
        var resultado = await cambiar.EjecutarAsync(
            new CambiarEstadoDiagnosticoComando(id, solicitud.Estado, solicitud.Motivo, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Agrega una intervención NIC a un diagnóstico activo. El médico solo puede agregar la NIC 7710.</summary>
    /// <param name="id">Diagnóstico del plan.</param>
    /// <param name="solicitud">Código y frecuencia.</param>
    /// <param name="agregar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="201">Intervención planificada.</response>
    /// <response code="400">Código o frecuencia inválidos.</response>
    /// <response code="403">Un médico intentó agregar otra NIC distinta de la 7710 (<c>nnn-solo-enfermeria</c>).</response>
    /// <response code="404">El diagnóstico no existe.</response>
    /// <response code="409">El diagnóstico está cerrado o la intervención ya está planificada.</response>
    [HttpPost("plan-cuidados/diagnosticos/{id:guid}/intervenciones")]
    [ProducesResponseType<IntervencionPlanDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<IntervencionPlanDto>> AgregarIntervencion(
        Guid id, AgregarIntervencionSolicitud solicitud, [FromServices] AgregarIntervencion agregar, CancellationToken ct)
    {
        var resultado = await agregar.EjecutarAsync(
            new AgregarIntervencionComando(id, solicitud.Codigo, solicitud.Frecuencia, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? StatusCode(StatusCodes.Status201Created, resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Registra que se ejecutó una intervención (quién y cuándo; por qué alerta y una observación, opcionales).</summary>
    /// <param name="id">Intervención del plan.</param>
    /// <param name="solicitud">Observación y alerta, opcionales.</param>
    /// <param name="registrar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="201">Ejecución registrada.</response>
    /// <response code="400">Observación de más de 500 caracteres o alerta de otra hospitalización.</response>
    /// <response code="403">Un médico solo registra la NIC 7710.</response>
    /// <response code="404">La intervención no existe.</response>
    /// <response code="409">La intervención o su diagnóstico ya no están activos.</response>
    [HttpPost("plan-cuidados/intervenciones/{id:guid}/ejecuciones")]
    [ProducesResponseType<EjecucionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EjecucionDto>> Ejecutar(
        Guid id, RegistrarEjecucionSolicitud solicitud, [FromServices] RegistrarEjecucion registrar, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(
            new RegistrarEjecucionComando(id, solicitud.Observacion, solicitud.AlertaId, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? StatusCode(StatusCodes.Status201Created, resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Valora un resultado NOC (escala de 1 a 5) por indicador. Una valoración no se edita: se corrige con otra nueva.</summary>
    /// <remarks>La respuesta trae la <c>puntuacionSugerida</c> de cada indicador vital (a partir de los puntos NEWS2 de la última evaluación).</remarks>
    /// <param name="id">Resultado del plan.</param>
    /// <param name="solicitud">Indicadores con su puntuación.</param>
    /// <param name="registrar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="201">Valoración registrada.</response>
    /// <response code="400">Puntuación fuera de 1 a 5 o indicador que no es de este resultado.</response>
    /// <response code="404">El resultado no existe.</response>
    /// <response code="409">El diagnóstico ya está cerrado.</response>
    [HttpPost("plan-cuidados/resultados/{id:guid}/valoraciones")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<ValoracionRegistradaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ValoracionRegistradaDto>> Valorar(
        Guid id, RegistrarValoracionSolicitud solicitud, [FromServices] RegistrarValoracion registrar, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(new RegistrarValoracionComando(
            id, solicitud.Indicadores?.Select(i => new IndicadorValorado(i?.Codigo, i?.Puntuacion)).ToList(),
            User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? StatusCode(StatusCodes.Status201Created, resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Indicadores de un resultado con la puntuación que propone el motor, para mostrarla antes de valorar.</summary>
    /// <param name="id">Resultado del plan.</param>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Indicadores con su propuesta (null si no es vital o no se midió) y la última puntuación.</response>
    /// <response code="404">El resultado no existe.</response>
    [HttpGet("plan-cuidados/resultados/{id:guid}/valoracion-sugerida")]
    [ProducesResponseType<ValoracionSugeridaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ValoracionSugeridaDto>> ValoracionSugerida(Guid id, [FromServices] ObtenerValoracionSugerida obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(new ObtenerValoracionSugeridaConsulta(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
