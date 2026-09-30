using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Dispositivos;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Inventario de wearables ESP32 y su estado.</summary>
[ApiController]
[Route("api/v1/dispositivos")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class DispositivosController : ControllerBase
{
    /// <summary>Registra un dispositivo; queda Disponible.</summary>
    /// <response code="201">Dispositivo creado.</response>
    /// <response code="400">Código inválido (patrón <c>^[A-Z0-9-]{3,32}$</c>).</response>
    /// <response code="409">Ya existe un dispositivo con ese código.</response>
    [HttpPost]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<DispositivoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DispositivoDto>> Registrar(
        RegistrarDispositivoSolicitud solicitud, [FromServices] RegistrarDispositivo registrarDispositivo, CancellationToken ct)
    {
        var resultado = await registrarDispositivo.EjecutarAsync(new RegistrarDispositivoComando(solicitud.Codigo ?? string.Empty, solicitud.Descripcion), ct);
        return resultado.EsExito ? StatusCode(StatusCodes.Status201Created, resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Envía el dispositivo a mantenimiento, lo da de baja o lo reactiva.</summary>
    /// <remarks>Un dispositivo vinculado a un paciente debe liberarse primero desde la ficha del paciente.</remarks>
    /// <response code="200">Estado actualizado.</response>
    /// <response code="400">Estado desconocido o no administrativo.</response>
    /// <response code="404">El dispositivo no existe.</response>
    /// <response code="409">Transición inválida desde el estado actual.</response>
    [HttpPatch("{id:guid}/estado")]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<DispositivoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DispositivoDto>> CambiarEstado(
        Guid id, CambiarEstadoDispositivoSolicitud solicitud, [FromServices] CambiarEstadoDispositivo cambiarEstado, CancellationToken ct)
    {
        var resultado = await cambiarEstado.EjecutarAsync(new CambiarEstadoDispositivoComando(id, solicitud.Estado ?? string.Empty), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Lista los dispositivos; si están asignados, indica la cama (no el paciente).</summary>
    /// <param name="listarDispositivos">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="estado">Filtro opcional: <c>Disponible</c>, <c>Asignado</c>, <c>Mantenimiento</c> o <c>DadoDeBaja</c>.</param>
    /// <response code="200">Dispositivos.</response>
    /// <response code="400">Estado desconocido.</response>
    [HttpGet]
    [Authorize(Policy = Politicas.AdministradorOPersonalClinico)]
    [ProducesResponseType<IReadOnlyList<DispositivoDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<DispositivoDto>>> Listar(
        [FromServices] ListarDispositivos listarDispositivos, CancellationToken ct, [FromQuery] string? estado = null)
    {
        var resultado = await listarDispositivos.EjecutarAsync(estado, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
