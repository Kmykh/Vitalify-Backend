using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Sistema;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Estado del sistema para el administrador. No expone datos de pacientes.</summary>
[ApiController]
[Route("api/v1/sistema")]
[Authorize(Policy = Politicas.SoloAdministrador)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class SistemaController : ControllerBase
{
    /// <summary>Cuánto ocupa la base y cada tabla, para vigilar el límite del plan gratuito de Supabase (500 MB).</summary>
    /// <remarks>
    /// <c>bytesBase</c> es toda la base (<c>pg_database_size</c>), que es lo que cuenta Supabase; cada tabla trae su tamaño
    /// con índices (<c>pg_total_relation_size</c>) y las filas aproximadas. Solo tamaños: ningún dato clínico.
    /// </remarks>
    /// <param name="consultar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Tamaños, de la tabla más grande a la más pequeña.</response>
    [HttpGet("almacenamiento")]
    [ProducesResponseType<AlmacenamientoDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AlmacenamientoDto>> Almacenamiento([FromServices] ConsultarAlmacenamiento consultar, CancellationToken ct) =>
        Ok(await consultar.EjecutarAsync(ct));
}
