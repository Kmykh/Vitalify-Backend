using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Turnos;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Turnos de enfermería (hora de Lima).</summary>
[ApiController]
[Route("api/v1/turnos")]
[Authorize(Policy = Politicas.PersonalClinico)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class TurnosController : ControllerBase
{
    /// <summary>Quién está de turno ahora en el servicio (por defecto, el de quien consulta).</summary>
    /// <remarks>Si no hay programación para este momento, <c>esRespaldo</c> es true y son todas las enfermeras activas del servicio.</remarks>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="servicioId">Servicio; si falta, el del usuario.</param>
    /// <response code="200">Enfermeras de turno.</response>
    /// <response code="400">Falta el servicio.</response>
    /// <response code="404">El servicio no existe.</response>
    [HttpGet("actual")]
    [ProducesResponseType<TurnoActualDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TurnoActualDto>> Actual([FromServices] ObtenerTurnoActual obtener, CancellationToken ct, [FromQuery] Guid? servicioId = null)
    {
        var resultado = await obtener.EjecutarAsync(servicioId, User.IdUsuario() ?? Guid.Empty, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Mis turnos programados (por defecto, de hoy a 30 días; como máximo 62 días).</summary>
    /// <param name="listar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="desde">Fecha local <c>AAAA-MM-DD</c>.</param>
    /// <param name="hasta">Fecha local <c>AAAA-MM-DD</c>.</param>
    /// <response code="200">Turnos, en orden.</response>
    /// <response code="400">Rango inválido.</response>
    [HttpGet("mios")]
    [ProducesResponseType<IReadOnlyList<MiTurnoDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<MiTurnoDto>>> Mios(
        [FromServices] ListarMisTurnos listar, CancellationToken ct, [FromQuery] DateOnly? desde = null, [FromQuery] DateOnly? hasta = null)
    {
        var resultado = await listar.EjecutarAsync(User.IdUsuario() ?? Guid.Empty, desde, hasta, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
