using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Usuarios;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Gestión de cuentas (HU01). Solo administradores.</summary>
[ApiController]
[Route("api/v1/usuarios")]
[Authorize(Policy = Politicas.SoloAdministrador)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public class UsuariosController : ControllerBase
{
    /// <summary>Registra una cuenta de médico o enfermera (HU01).</summary>
    /// <remarks>Los administradores no se crean por la API: el primero se crea con la semilla del .env.</remarks>
    /// <response code="201">Cuenta creada. <c>Location</c> apunta al usuario.</response>
    /// <response code="400">Datos inválidos (correo, contraseña débil, rol distinto de Medico o Enfermera...).</response>
    /// <response code="409">El correo ya está en uso.</response>
    [HttpPost]
    [ProducesResponseType<UsuarioDetalleDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UsuarioDetalleDto>> Registrar(
        RegistrarUsuarioSolicitud solicitud, [FromServices] RegistrarUsuario registrarUsuario, CancellationToken ct)
    {
        var resultado = await registrarUsuario.EjecutarAsync(
            new RegistrarUsuarioComando(
                solicitud.Nombre ?? string.Empty, solicitud.Correo ?? string.Empty, solicitud.Contrasena ?? string.Empty,
                solicitud.Rol ?? string.Empty, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip(), solicitud.ServicioId),
            ct);

        return resultado.EsExito
            ? CreatedAtAction(nameof(ObtenerPorId), new { id = resultado.Valor.Id }, resultado.Valor)
            : this.Problema(resultado.Error);
    }

    /// <summary>Lista paginada de usuarios, ordenada por nombre.</summary>
    /// <param name="pagina">Número de página, desde 1.</param>
    /// <param name="tamano">Elementos por página, de 1 a 100.</param>
    /// <param name="listarUsuarios">Caso de uso.</param>
    /// <param name="ct">Cancelación.</param>
    /// <response code="200">Página de usuarios.</response>
    /// <response code="400">Paginación inválida.</response>
    [HttpGet]
    [ProducesResponseType<PaginaRespuesta<UsuarioDetalleDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginaRespuesta<UsuarioDetalleDto>>> Listar(
        [FromServices] ListarUsuarios listarUsuarios, CancellationToken ct, [FromQuery] int pagina = 1, [FromQuery] int tamano = 20)
    {
        var resultado = await listarUsuarios.EjecutarAsync(new ListarUsuariosConsulta(pagina, tamano), ct);
        return resultado.EsExito ? Ok(PaginaRespuesta<UsuarioDetalleDto>.Desde(resultado.Valor)) : this.Problema(resultado.Error);
    }

    /// <summary>Detalle de un usuario.</summary>
    /// <response code="200">Usuario encontrado.</response>
    /// <response code="404">No existe.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UsuarioDetalleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UsuarioDetalleDto>> ObtenerPorId(Guid id, [FromServices] ObtenerUsuarioPorId obtenerUsuario, CancellationToken ct)
    {
        var resultado = await obtenerUsuario.EjecutarAsync(id, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Asigna el servicio de una enfermera o un médico (define sus turnos y qué alertas ve).</summary>
    /// <response code="200">Servicio asignado.</response>
    /// <response code="400">El servicio no existe o el usuario es administrador.</response>
    /// <response code="404">El usuario no existe.</response>
    [HttpPut("{id:guid}/servicio")]
    [ProducesResponseType<UsuarioDetalleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UsuarioDetalleDto>> AsignarServicio(
        Guid id, AsignarServicioSolicitud solicitud, [FromServices] Application.Servicios.AsignarServicio asignar, CancellationToken ct)
    {
        var resultado = await asignar.EjecutarAsync(
            new Application.Servicios.AsignarServicioComando(id, solicitud.ServicioId, User.IdUsuario() ?? Guid.Empty, HttpContext.Ip()), ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
