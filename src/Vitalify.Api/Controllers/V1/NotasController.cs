using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Notas;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Corrección y versiones de las notas clínicas (HU22). No existe borrado (NTS N° 139-MINSA/2018/DGAIN).</summary>
[ApiController]
[Route("api/v1/notas")]
[Authorize(Policy = Politicas.PersonalClinico)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class NotasController : ControllerBase
{
    /// <summary>Corrige una nota: solo su autor y dentro de las 24 h desde que la escribió.</summary>
    /// <remarks>
    /// El texto anterior queda como versión (<c>GET /notas/{id}/versiones</c>) y la nota guarda la fecha de la edición.
    /// Si el texto y el tipo no cambian, no se crea una versión.
    /// </remarks>
    /// <response code="200">Nota corregida.</response>
    /// <response code="400">Tipo o texto inválidos.</response>
    /// <response code="403">La nota es de otro autor (<c>nota-de-otro-autor</c>).</response>
    /// <response code="404">La nota no existe.</response>
    /// <response code="409">Pasaron las 24 h (<c>nota-fuera-de-plazo</c>) o se corrigió a la vez desde otra sesión.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<NotaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NotaDto>> Editar(Guid id, EditarNotaSolicitud solicitud, [FromServices] EditarNota editar, CancellationToken ct)
    {
        var resultado = await editar.EjecutarAsync(
            new EditarNotaComando(id, solicitud.Tipo, solicitud.Texto, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Versiones anteriores de una nota, de la más reciente a la original.</summary>
    /// <response code="200">Versiones (vacía si nunca se corrigió).</response>
    /// <response code="404">La nota no existe.</response>
    [HttpGet("{id:guid}/versiones")]
    [ProducesResponseType<VersionesNotaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VersionesNotaDto>> Versiones(Guid id, [FromServices] ListarVersionesNota listar, CancellationToken ct)
    {
        var resultado = await listar.EjecutarAsync(new ListarVersionesNotaConsulta(id, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
