using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Clinica;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Telemetria;

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
            solicitud.DiagnosticoIngreso ?? string.Empty, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip(), solicitud.ConfusionBasal), ct);

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
    /// <c>ultimoNews2</c>, <c>ultimoMews</c> y <c>nivelRiesgo</c> (<c>bajo</c>, <c>bajo-medio</c>, <c>medio</c>, <c>alto</c> o
    /// <c>sin-datos</c>) vienen de la última evaluación; <c>evaluacionCompleta</c> es false si el puntaje es parcial.
    /// <c>senal</c> es <c>con-datos</c>, <c>sin-datos</c> o <c>sin-senal</c> (no llega nada en el doble de la vigencia) y
    /// <c>conexionSensor</c>, <c>en-linea</c>, <c>fuera-de-linea</c> o <c>desconocida</c> (tópico MQTT de estado). Si no hay
    /// pacientes, <c>items</c> está vacío y <c>mensaje</c> lo indica.
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

    /// <summary>Estado actual de los signos vitales del paciente (HU10, HU11 y HU12).</summary>
    /// <remarks>
    /// Cada variable trae su último valor válido, cuándo se midió y su estado: <c>vigente</c>,
    /// <c>pendiente-actualizacion</c> (no llega hace más de la vigencia, 90 s por defecto) o <c>sin-datos</c>.
    /// </remarks>
    /// <response code="200">Signos actuales.</response>
    /// <response code="404">El paciente no existe o no tiene una hospitalización activa.</response>
    [HttpGet("{id:guid}/signos/actual")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<SignosActualesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SignosActualesDto>> SignosActuales(Guid id, [FromServices] ObtenerSignosActuales obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(id, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Lecturas del wearable de la hospitalización activa en un rango de tiempo, para los gráficos.</summary>
    /// <remarks>
    /// Devuelve las lecturas guardadas con <c>medidoEn</c> entre <c>desde</c> y <c>hasta</c> (inclusive), de la más
    /// antigua a la más reciente. Sin parámetros, la última hora. El rango máximo es de 24 h. Cada lectura trae solo las
    /// variables válidas que midió el sensor; las demás vienen en null.
    /// </remarks>
    /// <param name="id">Paciente.</param>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="desde">Inicio (UTC). Por defecto, una hora antes de <c>hasta</c>.</param>
    /// <param name="hasta">Fin (UTC). Por defecto, ahora.</param>
    /// <response code="200">Lecturas del rango (posiblemente ninguna).</response>
    /// <response code="400">Rango invertido o de más de 24 h.</response>
    /// <response code="404">El paciente no existe o no tiene una hospitalización activa.</response>
    [HttpGet("{id:guid}/signos/historial")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<HistorialSignosDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HistorialSignosDto>> HistorialSignos(
        Guid id, [FromServices] ObtenerHistorialSignos obtener, CancellationToken ct,
        [FromQuery] DateTime? desde = null, [FromQuery] DateTime? hasta = null)
    {
        var resultado = await obtener.EjecutarAsync(new ObtenerHistorialSignosConsulta(id, desde, hasta), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Última evaluación de riesgo NEWS2 y MEWS del paciente, con el desglose por parámetro.</summary>
    /// <remarks>
    /// Se recalcula con cada lectura del wearable y con cada observación. El wearable mide FC, SpO2, temperatura y FR:
    /// sin una toma manual de presión, conciencia y oxígeno, el puntaje es <b>parcial</b> (<c>completo: false</c>) y
    /// <c>faltantes</c> dice qué falta. La evaluación trae <c>recomendacion</c>, <c>proximaTomaManualEn</c> y
    /// <c>avisos</c>; la respuesta, <c>escalaSpo2</c> y <c>ultimoBrazoPa</c> (para sugerir el mismo brazo).
    /// <c>ultimaEvaluacion</c> es null si aún no hay ninguna.
    /// </remarks>
    /// <response code="200">Riesgo actual.</response>
    /// <response code="404">El paciente no existe o no tiene una hospitalización activa.</response>
    [HttpGet("{id:guid}/riesgo")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<RiesgoPacienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiesgoPacienteDto>> Riesgo(Guid id, [FromServices] ObtenerRiesgoPaciente obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(id, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Registra una toma manual (asistente de la guía) y recalcula el riesgo.</summary>
    /// <remarks>
    /// Presión con 1 a 3 lecturas (se guarda el promedio; si 2 lecturas difieren en más de 10 mmHg, pide una tercera),
    /// conciencia por respuestas (el backend calcula el ACVPU), oxígeno con dispositivo y flujo, FR si el sensor no tiene
    /// una vigente, contexto y preocupación clínica. Siguen aceptándose los campos planos de la fase 4. Cada valor cuenta
    /// para NEWS2/MEWS durante 4 h; gana el más reciente entre la toma y el sensor. Ver <c>GET /api/v1/guias/toma-manual</c>.
    /// </remarks>
    /// <response code="201">Toma registrada, con la evaluación, la recomendación, la próxima toma y los avisos.</response>
    /// <response code="400">Sin valores, valores imposibles, respuestas incompletas, lecturas que piden una tercera o fecha fuera de rango.</response>
    /// <response code="404">El paciente no existe o no tiene una hospitalización activa.</response>
    [HttpPost("{id:guid}/observaciones")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<ObservacionRegistradaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ObservacionRegistradaDto>> RegistrarObservacion(
        Guid id, RegistrarObservacionSolicitud solicitud, [FromServices] RegistrarObservacion registrar, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(
            new RegistrarObservacionComando(id, solicitud.AComando(), User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);

        return resultado.EsExito
            ? CreatedAtAction(nameof(Riesgo), new { id }, resultado.Valor)
            : this.Problema(resultado.Error);
    }

    /// <summary>El médico indica la escala de SpO2 de NEWS2 del paciente.</summary>
    /// <remarks>
    /// La escala 2 es para la insuficiencia respiratoria hipercápnica (objetivo 88–92 %). Se guarda en la hospitalización
    /// activa, queda auditada como <c>EscalaSpo2Cambiada</c> (sin el motivo) y el riesgo se recalcula al instante.
    /// </remarks>
    /// <response code="200">Escala cambiada, con el riesgo recalculado.</response>
    /// <response code="400">Escala desconocida o motivo inválido.</response>
    /// <response code="404">El paciente no existe o no tiene una hospitalización activa.</response>
    [HttpPut("{id:guid}/escala-spo2")]
    [Authorize(Policy = Politicas.SoloMedico)]
    [ProducesResponseType<RiesgoPacienteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiesgoPacienteDto>> CambiarEscalaSpo2(
        Guid id, CambiarEscalaSpo2Solicitud solicitud, [FromServices] CambiarEscalaSpo2 cambiar, CancellationToken ct)
    {
        var resultado = await cambiar.EjecutarAsync(
            new CambiarEscalaSpo2Comando(id, solicitud.Escala, solicitud.Motivo, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Vincula un sensor disponible a la cama del paciente (HU06), sin QR.</summary>
    /// <remarks>
    /// Vía manual de respaldo de <c>POST /vinculaciones</c>: invalida el QR del sensor y le publica "vinculado".
    /// Repetir la vinculación del mismo sensor es idempotente.
    /// </remarks>
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

    /// <summary>Desvincula el sensor del paciente; el sensor vuelve a Disponible y muestra un QR nuevo (HU06).</summary>
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

    /// <summary>Alertas del paciente en el servicio de quien consulta, con los tiempos de respuesta.</summary>
    /// <remarks><c>segundosHastaTomada</c> y <c>segundosHastaAtendida</c> se miden desde que se creó la alerta.</remarks>
    /// <response code="200">Alertas, la más reciente primero.</response>
    /// <response code="403">El usuario no tiene servicio asignado.</response>
    /// <response code="404">El paciente no existe.</response>
    [HttpGet("{id:guid}/alertas")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<IReadOnlyList<Application.Alertas.AlertaDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<Application.Alertas.AlertaDto>>> Alertas(
        Guid id, [FromServices] Application.Alertas.ListarAlertasPaciente listar, CancellationToken ct)
    {
        var resultado = await listar.EjecutarAsync(id, User.IdUsuario() ?? Guid.Empty, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
