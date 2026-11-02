using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Clinica;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Guías clínicas versionadas con las que la app arma sus asistentes.</summary>
[ApiController]
[Route("api/v1/guias")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class GuiasController : ControllerBase
{
    /// <summary>Guía del asistente de toma manual (oxígeno, conciencia, presión, FR, contexto y preocupación clínica).</summary>
    /// <remarks>
    /// Por sección trae las preguntas (con su <c>campo</c> en el cuerpo de <c>POST /pacientes/{id}/observaciones</c>, el
    /// tipo, las opciones y cuándo mostrarlas), las recomendaciones y sus fuentes (MINSA, INS, leyes peruanas y NEWS2). La
    /// <c>version</c> se envía como <c>versionGuia</c> en cada observación. El encabezado <c>ETag</c> es la versión.
    /// </remarks>
    /// <response code="200">Guía vigente (JSON).</response>
    [HttpGet("toma-manual")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult TomaManual([FromServices] ObtenerGuiaTomaManual obtener)
    {
        var guia = obtener.Ejecutar();
        Response.Headers.ETag = $"\"{guia.Version}\"";
        return Content(guia.Json, "application/json; charset=utf-8");
    }
}
