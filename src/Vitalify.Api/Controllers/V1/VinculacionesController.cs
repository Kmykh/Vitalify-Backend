using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Vinculacion;

namespace Vitalify.Api.Controllers.V1;

/// <summary>
/// Vinculación del wearable por QR (HU06): la enfermera escanea el QR que muestra el sensor y, en el mismo paso,
/// registra o elige al paciente, la cama y la toma inicial. El QR no lleva datos del paciente.
/// </summary>
[ApiController]
[Route("api/v1/vinculaciones")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class VinculacionesController(ILogger<VinculacionesController> logger) : ControllerBase
{
    /// <summary>Valida el QR del wearable y abre la vinculación.</summary>
    /// <remarks>
    /// Si el código es el vigente del sensor y el sensor está en línea, devuelve un <c>tokenVinculacion</c> de un solo
    /// uso (5 minutos) y el wearable pasa a "emparejando". Un QR vencido, ya usado o inventado responde siempre el mismo
    /// 404. Al quinto código equivocado sobre un mismo sensor, su QR se invalida y se publica uno nuevo. Hasta 10
    /// escaneos por minuto por usuario.
    /// </remarks>
    /// <response code="200">Conexión establecida con el sensor.</response>
    /// <response code="400">Falta el contenido del QR.</response>
    /// <response code="404">QR no válido, ya usado o vencido.</response>
    /// <response code="409">El sensor no está en línea (<c>sensor-sin-conexion</c>).</response>
    /// <response code="429">Demasiados escaneos en un minuto.</response>
    [HttpPost("escaneo")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [EnableRateLimiting(LimitesDeSolicitudes.EscaneoQr)]
    [ProducesResponseType<EscaneoQrDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<EscaneoQrDto>> Escanear(EscanearQrSolicitud solicitud, [FromServices] EscanearQr escanear, CancellationToken ct)
    {
        var resultado = await escanear.EjecutarAsync(new EscanearQrComando(solicitud.Qr, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Confirma la vinculación: ingreso, sensor, observación inicial y primer NEWS2/MEWS.</summary>
    /// <remarks>
    /// Con <c>paciente.pacienteId</c> se usa ese paciente (409 si ya está hospitalizado); sin él, se aplican las reglas
    /// de <c>POST /pacientes</c> (si el documento ya existe, se reutiliza). En la observación inicial son obligatorios
    /// el oxígeno, la conciencia y al menos una lectura de presión. Ingreso y vinculación se guardan en una sola
    /// transacción; si después falla el guardado de la observación, responde 201 igual con un aviso para registrarla de
    /// nuevo. El wearable recibe "vinculado" con la cama y las iniciales.
    /// </remarks>
    /// <response code="201">Paciente hospitalizado y sensor vinculado. <c>Location</c> apunta a su ficha.</response>
    /// <response code="400">Datos inválidos (un error por campo; los del paciente van como <c>paciente.campo</c>).</response>
    /// <response code="404">Token vencido o inválido, o el paciente indicado no existe.</response>
    /// <response code="409">La cama está ocupada, el paciente ya está hospitalizado o el sensor dejó de estar disponible.</response>
    [HttpPost("confirmar")]
    [Authorize(Policy = Politicas.SoloEnfermera)]
    [ProducesResponseType<VinculacionConfirmadaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VinculacionConfirmadaDto>> Confirmar(
        ConfirmarVinculacionSolicitud solicitud, [FromServices] ConfirmarVinculacion confirmar, CancellationToken ct)
    {
        var p = solicitud.Paciente;
        var resultado = await confirmar.EjecutarAsync(new ConfirmarVinculacionComando(
            solicitud.TokenVinculacion,
            p is null ? null : new PacienteVinculacion(p.PacienteId, p.NombreCompleto, p.TipoDocumento, p.NumeroDocumento, p.FechaNacimiento, p.Edad, p.ConfusionBasal),
            solicitud.CamaId,
            solicitud.DiagnosticoIngreso,
            solicitud.ObservacionInicial?.AComando(),
            User.IdUsuario() ?? Guid.Empty,
            HttpContext.Ip()), ct);

        if (!resultado.EsExito)
        {
            return this.Problema(resultado.Error);
        }

        var (respuesta, fallo) = resultado.Valor;
        if (fallo is not null)
        {
            // Solo ids: el paciente ya quedó vinculado y la enfermera recibe el aviso de registrar la toma de nuevo.
            logger.LogError(fallo, "Vinculación por QR: la observación inicial de la hospitalización {Hospitalizacion} no se guardó.",
                respuesta.Paciente.HospitalizacionActiva?.Id);
        }

        return Created($"/api/v1/pacientes/{respuesta.Paciente.Id}", respuesta);
    }
}
