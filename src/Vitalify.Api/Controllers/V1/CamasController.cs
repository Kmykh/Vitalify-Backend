using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Camas;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Catálogo de camas. Muestra si cada cama está ocupada, pero nunca quién la ocupa.</summary>
[ApiController]
[Route("api/v1/camas")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class CamasController : ControllerBase
{
    /// <summary>Registra una cama en el catálogo.</summary>
    /// <response code="201">Cama creada.</response>
    /// <response code="400">Código o servicio inválidos.</response>
    /// <response code="409">Ya existe una cama con ese código.</response>
    [HttpPost]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<CamaDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CamaDto>> Registrar(RegistrarCamaSolicitud solicitud, [FromServices] RegistrarCama registrarCama, CancellationToken ct)
    {
        var resultado = await registrarCama.EjecutarAsync(new RegistrarCamaComando(solicitud.Codigo ?? string.Empty, solicitud.Servicio ?? string.Empty), ct);
        return resultado.EsExito ? StatusCode(StatusCodes.Status201Created, resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Lista las camas ordenadas por código.</summary>
    /// <param name="listarCamas">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="soloDisponibles">Si es <c>true</c>, solo las camas activas y libres.</param>
    /// <response code="200">Camas con su ocupación.</response>
    [HttpGet]
    [Authorize(Policy = Politicas.AdministradorOPersonalClinico)]
    [ProducesResponseType<IReadOnlyList<CamaDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CamaDto>>> Listar(
        [FromServices] ListarCamas listarCamas, CancellationToken ct, [FromQuery] bool soloDisponibles = false) =>
        Ok((await listarCamas.EjecutarAsync(soloDisponibles, ct)).Valor);
}
