using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Configuracion;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Endpoint de prueba para comprobar que la API responde.</summary>
[ApiController]
[Route("api/v1/ping")]
[AllowAnonymous]
public class PingController(TimeProvider timeProvider) : ControllerBase
{
    /// <summary>Devuelve el nombre del servicio, su versión y la hora actual en UTC.</summary>
    /// <response code="200">La API está en línea.</response>
    [HttpGet]
    [ProducesResponseType<PingRespuesta>(StatusCodes.Status200OK)]
    public ActionResult<PingRespuesta> Get() =>
        Ok(new PingRespuesta(InfoServicio.Nombre, InfoServicio.Version, timeProvider.GetUtcNow().UtcDateTime));
}

/// <summary>Respuesta de <c>GET /api/v1/ping</c>.</summary>
/// <param name="Servicio">Nombre del servicio.</param>
/// <param name="Version">Versión de la API.</param>
/// <param name="Hora">Hora actual del servidor en UTC.</param>
public record PingRespuesta(string Servicio, string Version, DateTime Hora);
