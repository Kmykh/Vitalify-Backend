using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Alertas;
using Vitalify.Application.Historial;
using Vitalify.Application.Notas;

namespace Vitalify.Api.Controllers.V1;

/// <summary>
/// Historial del paciente para el médico (HU19) y notas clínicas (HU22). Por defecto se consulta la hospitalización
/// activa o, si no hay, la última; <c>hospitalizacionId</c> elige otra. Lo lee cualquier médico o enfermera, aunque el
/// paciente sea de otro servicio, y cada consulta queda auditada como <c>ConsultaHistorialPaciente</c>.
/// </summary>
[ApiController]
[Route("api/v1/pacientes")]
[Authorize(Policy = Politicas.PersonalClinico)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class HistorialPacienteController : ControllerBase
{
    /// <summary>Todos los episodios del paciente: ingreso, egreso y motivo, cama, servicio, diagnóstico y sensores usados.</summary>
    /// <response code="200">Episodios, del más reciente al más antiguo.</response>
    /// <response code="404">El paciente no existe.</response>
    [HttpGet("{id:guid}/hospitalizaciones")]
    [ProducesResponseType<HospitalizacionesPacienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HospitalizacionesPacienteDto>> Hospitalizaciones(
        Guid id, [FromServices] ListarHospitalizacionesPaciente listar, CancellationToken ct)
    {
        var resultado = await listar.EjecutarAsync(new ListarHospitalizacionesConsulta(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Lecturas del wearable de un episodio, para los gráficos (HU19).</summary>
    /// <remarks>
    /// Sin <c>desde</c> ni <c>hasta</c>, el episodio completo (hasta ahora si sigue hospitalizado). Sin <c>agrupar</c>
    /// devuelve cada lectura, en un rango de hasta 7 días; con <c>agrupar</c> devuelve, por intervalo, el promedio, el
    /// mínimo y el máximo de cada variable y si hubo una caída, y admite el episodio completo. Cada lectura trae solo
    /// las variables válidas que midió el sensor.
    /// </remarks>
    /// <param name="id">Paciente.</param>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="hospitalizacionId">Episodio; por defecto, el activo o el último.</param>
    /// <param name="desde">Inicio (UTC). Por defecto, el ingreso.</param>
    /// <param name="hasta">Fin (UTC). Por defecto, el egreso o ahora.</param>
    /// <param name="agrupar"><c>1m</c>, <c>5m</c>, <c>15m</c> o <c>1h</c>.</param>
    /// <response code="200">Lecturas o grupos del rango (posiblemente ninguno).</response>
    /// <response code="400">Rango invertido, de más de 7 días sin agrupar, o <c>agrupar</c> inválido.</response>
    /// <response code="404">El paciente no existe, nunca estuvo hospitalizado o el episodio no es suyo.</response>
    [HttpGet("{id:guid}/signos/historial")]
    [ProducesResponseType<HistorialSignosDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HistorialSignosDto>> HistorialSignos(
        Guid id, [FromServices] ObtenerHistorialSignos obtener, CancellationToken ct,
        [FromQuery] Guid? hospitalizacionId = null, [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null,
        [FromQuery] string? agrupar = null)
    {
        var resultado = await obtener.EjecutarAsync(
            new ObtenerHistorialSignosConsulta(id, desde, hasta, hospitalizacionId, agrupar, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Evaluaciones NEWS2 y MEWS de un episodio, con nivel, completo, faltantes y puntos por parámetro.</summary>
    /// <remarks>
    /// Mismo rango y mismos límites que el historial de signos. Con <c>agrupar</c>, por intervalo: cantidad de
    /// evaluaciones y de completas, y de cada puntaje el mínimo, el máximo, el último y el peor nivel.
    /// </remarks>
    /// <param name="id">Paciente.</param>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="hospitalizacionId">Episodio; por defecto, el activo o el último.</param>
    /// <param name="desde">Inicio (UTC). Por defecto, el ingreso.</param>
    /// <param name="hasta">Fin (UTC). Por defecto, el egreso o ahora.</param>
    /// <param name="agrupar"><c>5m</c>, <c>15m</c> o <c>1h</c>.</param>
    /// <response code="200">Evaluaciones o grupos del rango.</response>
    /// <response code="400">Rango invertido, de más de 7 días sin agrupar, o <c>agrupar</c> inválido.</response>
    /// <response code="404">El paciente no existe, nunca estuvo hospitalizado o el episodio no es suyo.</response>
    [HttpGet("{id:guid}/riesgo/historial")]
    [ProducesResponseType<HistorialRiesgoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HistorialRiesgoDto>> HistorialRiesgo(
        Guid id, [FromServices] ObtenerHistorialRiesgo obtener, CancellationToken ct,
        [FromQuery] Guid? hospitalizacionId = null, [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null,
        [FromQuery] string? agrupar = null)
    {
        var resultado = await obtener.EjecutarAsync(
            new ObtenerHistorialRiesgoConsulta(id, hospitalizacionId, desde, hasta, agrupar, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Tomas manuales de un episodio, con el nombre del profesional que las registró.</summary>
    /// <remarks>
    /// Cada toma trae las lecturas de presión, el checklist, el ACVPU, el oxígeno, el contexto y la preocupación
    /// clínica. Por defecto, todo el episodio; la más reciente primero.
    /// </remarks>
    /// <param name="id">Paciente.</param>
    /// <param name="listar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="hospitalizacionId">Episodio; por defecto, el activo o el último.</param>
    /// <param name="desde">Inicio (UTC). Por defecto, el ingreso.</param>
    /// <param name="hasta">Fin (UTC). Por defecto, el egreso o ahora.</param>
    /// <response code="200">Tomas del rango.</response>
    /// <response code="400">Rango invertido.</response>
    /// <response code="404">El paciente no existe, nunca estuvo hospitalizado o el episodio no es suyo.</response>
    [HttpGet("{id:guid}/observaciones")]
    [ProducesResponseType<ObservacionesPacienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ObservacionesPacienteDto>> Observaciones(
        Guid id, [FromServices] ListarObservacionesPaciente listar, CancellationToken ct,
        [FromQuery] Guid? hospitalizacionId = null, [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null)
    {
        var resultado = await listar.EjecutarAsync(
            new ListarObservacionesConsulta(id, hospitalizacionId, desde, hasta, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Resumen del episodio para la vista del médico.</summary>
    /// <remarks>
    /// Datos del paciente y del episodio, último riesgo, mínimos y máximos por variable (del sensor y de las tomas
    /// manuales, por separado), alertas por tipo con su tiempo medio de respuesta, alertas abiertas, últimas 5 notas y
    /// próxima toma manual.
    /// </remarks>
    /// <response code="200">Resumen.</response>
    /// <response code="404">El paciente no existe, nunca estuvo hospitalizado o el episodio no es suyo.</response>
    [HttpGet("{id:guid}/resumen")]
    [ProducesResponseType<ResumenPacienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResumenPacienteDto>> Resumen(
        Guid id, [FromServices] ObtenerResumenPaciente obtener, CancellationToken ct, [FromQuery] Guid? hospitalizacionId = null)
    {
        var resultado = await obtener.EjecutarAsync(new ObtenerResumenConsulta(id, hospitalizacionId, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Alertas de un episodio del paciente, con los tiempos de respuesta.</summary>
    /// <remarks><c>segundosHastaTomada</c> y <c>segundosHastaAtendida</c> se miden desde que se creó la alerta.</remarks>
    /// <response code="200">Alertas, la más reciente primero.</response>
    /// <response code="404">El paciente no existe, nunca estuvo hospitalizado o el episodio no es suyo.</response>
    [HttpGet("{id:guid}/alertas")]
    [ProducesResponseType<IReadOnlyList<AlertaDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AlertaDto>>> Alertas(
        Guid id, [FromServices] ListarAlertasPaciente listar, CancellationToken ct, [FromQuery] Guid? hospitalizacionId = null)
    {
        var resultado = await listar.EjecutarAsync(new ListarAlertasPacienteConsulta(id, hospitalizacionId, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Notas clínicas de un episodio, la más reciente primero, con su autor (HU22).</summary>
    /// <response code="200">Notas del episodio.</response>
    /// <response code="404">El paciente no existe, nunca estuvo hospitalizado o el episodio no es suyo.</response>
    [HttpGet("{id:guid}/notas")]
    [ProducesResponseType<NotasPacienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotasPacienteDto>> Notas(
        Guid id, [FromServices] ListarNotas listar, CancellationToken ct, [FromQuery] Guid? hospitalizacionId = null)
    {
        var resultado = await listar.EjecutarAsync(new ListarNotasConsulta(id, hospitalizacionId, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Registra una nota clínica en la hospitalización activa (HU22).</summary>
    /// <remarks>Queda con la fecha, la hora y el autor. No se puede borrar; su autor puede corregirla dentro de las 24 h.</remarks>
    /// <response code="201">Nota registrada.</response>
    /// <response code="400">Tipo o texto inválidos.</response>
    /// <response code="404">El paciente no existe.</response>
    /// <response code="409">El paciente no tiene una hospitalización activa.</response>
    [HttpPost("{id:guid}/notas")]
    [ProducesResponseType<NotaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NotaDto>> RegistrarNota(
        Guid id, RegistrarNotaSolicitud solicitud, [FromServices] RegistrarNota registrar, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(
            new RegistrarNotaComando(id, solicitud.Tipo, solicitud.Texto, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito
            ? Created($"/api/v1/pacientes/{id}/notas", resultado.Valor)
            : this.Problema(resultado.Error);
    }
}
