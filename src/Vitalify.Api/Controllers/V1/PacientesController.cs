using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Pacientes;

namespace Vitalify.Api.Controllers.V1;

/// <summary>
/// Pacientes hospitalizados: ingreso (HU05), sensor (HU06), monitoreo (HU07) y egreso (HU08).
/// Solo personal clínico; el administrador no ve datos de pacientes. No existe borrado.
/// </summary>
[ApiController]
[Route("api/v1/pacientes")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class PacientesController : ControllerBase
{
    /// <summary>Registra el ingreso de un paciente en una cama (HU05).</summary>
    /// <remarks>
    /// Si ya existe un paciente con ese documento, se reutiliza su registro sin modificar sus datos (reingreso).
    /// Envía <c>fechaNacimiento</c> o, si no se conoce, <c>edad</c>.
    /// </remarks>
    /// <response code="201">Paciente ingresado. <c>Location</c> apunta a su ficha.</response>
    /// <response code="400">Campos obligatorios vacíos o inválidos (un error por campo), o cama inexistente/inactiva.</response>
    /// <response code="409">La cama está ocupada o el paciente ya tiene una hospitalización activa.</response>
    [HttpPost]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<FichaPacienteDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FichaPacienteDto>> Ingresar(
        RegistrarIngresoSolicitud solicitud, [FromServices] RegistrarIngresoPaciente registrarIngreso, CancellationToken ct)
    {
        var resultado = await registrarIngreso.EjecutarAsync(new RegistrarIngresoPacienteComando(
            solicitud.NombreCompleto ?? string.Empty, solicitud.TipoDocumento ?? string.Empty, solicitud.NumeroDocumento ?? string.Empty,
            solicitud.FechaNacimiento, solicitud.Edad, solicitud.CamaId, solicitud.DiagnosticoIngreso ?? string.Empty,
            User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);

        return resultado.EsExito
            ? CreatedAtAction(nameof(Obtener), new { id = resultado.Valor.Id }, resultado.Valor)
            : this.Problema(resultado.Error);
    }

    /// <summary>Corrige los datos básicos del paciente y el diagnóstico de su hospitalización activa (HU05).</summary>
    /// <response code="200">Ficha actualizada.</response>
    /// <response code="400">Datos inválidos.</response>
    /// <response code="404">El paciente no existe o no tiene una hospitalización activa.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<FichaPacienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FichaPacienteDto>> Actualizar(
        Guid id, ActualizarPacienteSolicitud solicitud, [FromServices] ActualizarDatosPaciente actualizar, CancellationToken ct)
    {
        var resultado = await actualizar.EjecutarAsync(new ActualizarDatosPacienteComando(
            id, solicitud.NombreCompleto ?? string.Empty, solicitud.FechaNacimiento, solicitud.Edad,
            solicitud.DiagnosticoIngreso ?? string.Empty, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);

        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Pacientes con hospitalización activa, ordenados por cama.</summary>
    /// <param name="listar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="pagina">Número de página, desde 1.</param>
    /// <param name="tamano">Elementos por página, de 1 a 100.</param>
    /// <param name="buscar">Parte del nombre o de la cama, o el número de documento exacto.</param>
    /// <response code="200">Página de pacientes.</response>
    /// <response code="400">Paginación inválida.</response>
    [HttpGet]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<PaginaRespuesta<PacienteHospitalizadoDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginaRespuesta<PacienteHospitalizadoDto>>> Listar(
        [FromServices] ListarPacientesHospitalizados listar, CancellationToken ct,
        [FromQuery] int pagina = 1, [FromQuery] int tamano = 20, [FromQuery] string? buscar = null)
    {
        var resultado = await listar.EjecutarAsync(new ListarPacientesHospitalizadosConsulta(pagina, tamano, buscar), ct);
        return resultado.EsExito ? Ok(PaginaRespuesta<PacienteHospitalizadoDto>.Desde(resultado.Valor)) : this.Problema(resultado.Error);
    }

    /// <summary>Pacientes activos en monitoreo continuo, es decir, con un sensor vinculado (HU07).</summary>
    /// <remarks>
    /// <c>ultimoNews2</c>, <c>ultimoMews</c> y <c>nivelRiesgo</c> se completan en la fase 4; por ahora son
    /// <c>null</c> y <c>sin-datos</c>. Si no hay pacientes, <c>items</c> está vacío y <c>mensaje</c> lo indica.
    /// </remarks>
    /// <response code="200">Lista de monitoreo (posiblemente vacía).</response>
    [HttpGet("monitoreados")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<PacientesMonitoreadosRespuesta>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PacientesMonitoreadosRespuesta>> Monitoreados([FromServices] ListarPacientesMonitoreados listar, CancellationToken ct) =>
        Ok(PacientesMonitoreadosRespuesta.Desde((await listar.EjecutarAsync(ct)).Valor));

    /// <summary>Ficha del paciente con su hospitalización activa, cama y sensor. Queda auditada.</summary>
    /// <response code="200">Ficha (con <c>hospitalizacionActiva</c> null si ya egresó).</response>
    /// <response code="404">El paciente no existe.</response>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<FichaPacienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FichaPacienteDto>> Obtener(Guid id, [FromServices] ObtenerPaciente obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(new ObtenerPacienteConsulta(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Vincula un sensor disponible a la cama del paciente (HU06).</summary>
    /// <remarks>Repetir la vinculación del mismo sensor es idempotente.</remarks>
    /// <response code="200">Vinculación confirmada.</response>
    /// <response code="400">Falta el dispositivo.</response>
    /// <response code="404">El paciente o el dispositivo no existen.</response>
    /// <response code="409">
    /// El sensor está vinculado a otra cama (el detalle indica cuál), en mantenimiento o dado de baja; el paciente ya
    /// tiene otro sensor; o no tiene una hospitalización activa.
    /// </response>
    [HttpPost("{id:guid}/dispositivo")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<VinculacionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VinculacionDto>> VincularDispositivo(
        Guid id, VincularDispositivoSolicitud solicitud, [FromServices] VincularDispositivo vincular, CancellationToken ct)
    {
        var resultado = await vincular.EjecutarAsync(
            new VincularDispositivoComando(id, solicitud.DispositivoId, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Desvincula el sensor del paciente; el sensor vuelve a Disponible (HU06).</summary>
    /// <response code="204">Sensor liberado.</response>
    /// <response code="404">El paciente no existe, no está hospitalizado o no tiene sensor.</response>
    [HttpDelete("{id:guid}/dispositivo")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LiberarDispositivo(Guid id, [FromServices] LiberarDispositivo liberar, CancellationToken ct)
    {
        var resultado = await liberar.EjecutarAsync(new LiberarDispositivoComando(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? NoContent() : this.Problema(resultado.Error);
    }

    /// <summary>Registra el egreso del paciente (HU08).</summary>
    /// <remarks>
    /// Si tiene un sensor vinculado, lo libera en la misma transacción. La hospitalización queda archivada
    /// (Finalizada) y la cama libre; nada se borra.
    /// </remarks>
    /// <response code="200">Egreso registrado.</response>
    /// <response code="400">Motivo inválido.</response>
    /// <response code="404">El paciente no existe.</response>
    /// <response code="409">El paciente no tiene una hospitalización activa.</response>
    [HttpPost("{id:guid}/egreso")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<EgresoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EgresoDto>> RegistrarEgreso(
        Guid id, RegistrarEgresoSolicitud solicitud, [FromServices] RegistrarEgreso registrarEgreso, CancellationToken ct)
    {
        var resultado = await registrarEgreso.EjecutarAsync(new RegistrarEgresoComando(
            id, solicitud.Motivo ?? string.Empty, solicitud.Observacion, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
