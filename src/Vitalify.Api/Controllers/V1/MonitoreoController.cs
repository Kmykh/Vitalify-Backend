using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Monitoreo de pacientes (personal clínico).</summary>
[ApiController]
[Route("api/v1/monitoreo")]
public class MonitoreoController : ControllerBase
{
    /// <summary>Endpoint temporal para comprobar el acceso del personal clínico (HU03 E2).</summary>
    /// <response code="200">El rol tiene acceso.</response>
    /// <response code="401">Sin token o con un token inválido.</response>
    /// <response code="403">El rol no es Medico ni Enfermera.</response>
    [HttpGet("resumen")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<ResumenMonitoreoRespuesta>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public ActionResult<ResumenMonitoreoRespuesta> Resumen()
    {
        // TODO fase 6: reemplazar por el resumen real
        return Ok(new ResumenMonitoreoRespuesta("acceso permitido", User.Rol()));
    }
}
