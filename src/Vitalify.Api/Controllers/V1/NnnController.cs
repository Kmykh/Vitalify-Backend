using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Nnn;

namespace Vitalify.Api.Controllers.V1;

/// <summary>
/// Catálogo NNN (NANDA-I, NIC y NOC): solo código, etiqueta corta, edición y si una enfermera asesora lo verificó. No tiene
/// datos de pacientes. Ver <c>docs/nnn.md</c> (licencia y verificación).
/// </summary>
[ApiController]
[Route("api/v1/nnn")]
[Authorize(Policy = Politicas.AdministradorOPersonalClinico)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class NnnController : ControllerBase
{
    /// <summary>Busca en el catálogo por código o etiqueta (sin distinguir mayúsculas ni tildes), hasta 50 entradas.</summary>
    /// <param name="consultar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <param name="tipo"><c>nanda</c>, <c>nic</c> o <c>noc</c>.</param>
    /// <param name="q">Texto a buscar, por ejemplo <c>resp</c>; vacío lista todo.</param>
    /// <response code="200">Entradas activas, ordenadas por código.</response>
    /// <response code="400">El tipo no es nanda, nic ni noc.</response>
    [HttpGet("catalogo")]
    [ProducesResponseType<IReadOnlyList<EntradaCatalogoDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<EntradaCatalogoDto>>> Catalogo(
        [FromServices] ConsultarCatalogoNnn consultar, CancellationToken ct, [FromQuery] string? tipo = null, [FromQuery] string? q = null)
    {
        var resultado = await consultar.EjecutarAsync(new ConsultarCatalogoConsulta(tipo, q), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>NIC y NOC sugeridos por el catálogo para un diagnóstico NANDA-I, con los indicadores de cada NOC.</summary>
    /// <param name="codigo">Código NANDA-I, por ejemplo <c>00032</c>.</param>
    /// <param name="obtener">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Vínculos del diagnóstico.</response>
    /// <response code="404">El diagnóstico no está en el catálogo.</response>
    [HttpGet("diagnosticos/{codigo}/vinculos")]
    [ProducesResponseType<VinculosDiagnosticoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VinculosDiagnosticoDto>> Vinculos(string codigo, [FromServices] ObtenerVinculosDiagnostico obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(codigo, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Marca una entrada del catálogo como verificada por la enfermera asesora.</summary>
    /// <remarks>Idempotente. Desde entonces la semilla ya no la modifica. Queda auditado como <c>VerificarCatalogoNnn</c>.</remarks>
    /// <param name="tipo"><c>nanda</c>, <c>nic</c>, <c>noc</c> o <c>indicador</c>.</param>
    /// <param name="codigo">Código de la entrada.</param>
    /// <param name="verificar">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Entrada verificada.</response>
    /// <response code="400">Tipo o código con formato inválido.</response>
    /// <response code="404">La entrada no existe.</response>
    [HttpPut("catalogo/{tipo}/{codigo}/verificacion")]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<EntradaVerificadaDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntradaVerificadaDto>> Verificar(string tipo, string codigo, [FromServices] VerificarEntradaCatalogoNnn verificar, CancellationToken ct)
    {
        var resultado = await verificar.EjecutarAsync(new VerificarEntradaComando(tipo, codigo, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
