using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Servicios;
using Vitalify.Application.Turnos;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Servicios hospitalarios, su programación mensual de turnos y la entrega de turno.</summary>
[ApiController]
[Route("api/v1/servicios")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class ServiciosController : ControllerBase
{
    /// <summary>Catálogo de servicios.</summary>
    /// <response code="200">Servicios, por código.</response>
    [HttpGet]
    [Authorize(Policy = Politicas.AdministradorOPersonalClinico)]
    [ProducesResponseType<IReadOnlyList<ServicioDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ServicioDto>>> Listar([FromServices] ListarServicios listar, CancellationToken ct) =>
        Ok(await listar.EjecutarAsync(ct));

    /// <summary>Registra un servicio (por ejemplo EMERGENCIA o UVI).</summary>
    /// <response code="201">Servicio creado.</response>
    /// <response code="400">Código o nombre inválidos.</response>
    /// <response code="409">Ya existe un servicio con ese código.</response>
    [HttpPost]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<ServicioDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServicioDto>> Registrar(RegistrarServicioSolicitud solicitud, [FromServices] RegistrarServicio registrar, CancellationToken ct)
    {
        var resultado = await registrar.EjecutarAsync(new RegistrarServicioComando(solicitud.Codigo ?? string.Empty, solicitud.Nombre ?? string.Empty), ct);
        return resultado.EsExito ? StatusCode(StatusCodes.Status201Created, resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Carga la programación del mes con el formato de la planilla de papel (reemplaza el mes completo).</summary>
    /// <remarks>
    /// <c>letras</c> asigna cada letra a una enfermera activa del servicio. Cada día dice qué letras cubren cada turno
    /// (<c>GuardiaDiurna</c> 07–19, <c>GuardiaNocturna</c> 19–07, <c>Manana</c> 07–13, <c>Tarde</c> 13–19, hora de Lima). Una
    /// enfermera no puede tener dos turnos que se crucen. Queda auditada como <c>ProgramacionCargada</c>.
    /// </remarks>
    /// <response code="200">Programación cargada (la planilla resultante).</response>
    /// <response code="400">Errores por día y letra (<c>dias.5.C</c>, <c>letras.B</c>...).</response>
    /// <response code="404">El servicio no existe.</response>
    [HttpPut("{id:guid}/programacion/{anio:int}/{mes:int}")]
    [Authorize(Policy = Politicas.SoloAdministrador)]
    [ProducesResponseType<ProgramacionMesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProgramacionMesDto>> CargarProgramacion(
        Guid id, int anio, int mes, CargarProgramacionSolicitud solicitud, [FromServices] CargarProgramacion cargar, CancellationToken ct)
    {
        var resultado = await cargar.EjecutarAsync(new CargarProgramacionComando(
            id, anio, mes, solicitud.Letras,
            solicitud.Dias?.Select(d => new DiaPlanillaComando(d.Dia, d.TurnosComoLetras())).ToList(),
            User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>La planilla del mes, con letras y nombres de las enfermeras.</summary>
    /// <response code="200">Planilla (vacía si no se cargó).</response>
    /// <response code="404">El servicio no existe.</response>
    [HttpGet("{id:guid}/programacion/{anio:int}/{mes:int}")]
    [Authorize(Policy = Politicas.AdministradorOPersonalClinico)]
    [ProducesResponseType<ProgramacionMesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProgramacionMesDto>> ObtenerProgramacion(
        Guid id, int anio, int mes, [FromServices] ObtenerProgramacion obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(id, anio, mes, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Entrega de turno del servicio ("reporte de enfermería"), ordenada por riesgo.</summary>
    /// <remarks>
    /// Por paciente: cama, NEWS2 y MEWS actuales con su tendencia en la guardia (primero, máximo y último), alertas
    /// abiertas y atendidas en la guardia, próxima toma manual (y si está vencida), sensor y última preocupación clínica.
    /// </remarks>
    /// <response code="200">Entrega de turno.</response>
    /// <response code="403">El servicio no es el de quien consulta.</response>
    /// <response code="404">El servicio no existe.</response>
    [HttpGet("{id:guid}/entrega-turno")]
    [Authorize(Policy = Politicas.PersonalClinico)]
    [ProducesResponseType<EntregaTurnoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntregaTurnoDto>> EntregaTurno(Guid id, [FromServices] ObtenerEntregaTurno obtener, CancellationToken ct)
    {
        var resultado = await obtener.EjecutarAsync(id, User.IdUsuario() ?? Guid.Empty, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
