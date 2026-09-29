using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Vitalify.Api.Contratos;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Usuarios;

namespace Vitalify.Api.Controllers.V1;

/// <summary>Inicio, renovación y cierre de sesión (HU02 y HU04).</summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    /// <summary>Inicia sesión con correo y contraseña (HU02).</summary>
    /// <remarks>
    /// Devuelve un access token JWT (15 min) y un refresh token. El rol del usuario permite al cliente abrir el
    /// panel que le corresponde. Límite: 5 intentos por minuto por IP.
    /// </remarks>
    /// <response code="200">Sesión iniciada.</response>
    /// <response code="400">Faltan el correo o la contraseña.</response>
    /// <response code="401">Correo o contraseña incorrectos (mismo mensaje en ambos casos).</response>
    /// <response code="429">Se superó el límite de intentos.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LimiteDeLogin.Politica)]
    [ProducesResponseType<SesionIniciada>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SesionIniciada>> Login(LoginSolicitud solicitud, [FromServices] IniciarSesion iniciarSesion, CancellationToken ct)
    {
        var resultado = await iniciarSesion.EjecutarAsync(
            new IniciarSesionComando(solicitud.Correo ?? string.Empty, solicitud.Contrasena ?? string.Empty, HttpContext.Ip()), ct);

        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Renueva el access token y rota el refresh token (HU04 E2).</summary>
    /// <remarks>
    /// El refresh token enviado deja de servir y se devuelve uno nuevo. Si la sesión pasó 30 min sin usarse o
    /// 12 h desde el login, responde 401 con <c>type</c> <c>sesion-expirada</c>: el cliente debe volver al login.
    /// Reusar un refresh token ya rotado revoca todas las sesiones del usuario.
    /// </remarks>
    /// <response code="200">Nuevo par de tokens.</response>
    /// <response code="400">Falta el refresh token.</response>
    /// <response code="401">Refresh token revocado, desconocido o sesión expirada.</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<SesionIniciada>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SesionIniciada>> Refrescar(RefrescarSolicitud solicitud, [FromServices] RefrescarSesion refrescarSesion, CancellationToken ct)
    {
        var resultado = await refrescarSesion.EjecutarAsync(
            new RefrescarSesionComando(solicitud.RefreshToken ?? string.Empty, HttpContext.Ip()), ct);

        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }

    /// <summary>Cierra la sesión (HU04 E1).</summary>
    /// <remarks>
    /// Invalida el access token con el que se llama (su <c>jti</c> queda revocado hasta que expire) y, si se
    /// envía, revoca también el refresh token.
    /// </remarks>
    /// <response code="204">Sesión cerrada.</response>
    /// <response code="401">Sin token o con un token inválido.</response>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CerrarSesionSolicitud? solicitud,
        [FromServices] CerrarSesion cerrarSesion,
        CancellationToken ct)
    {
        var comando = new CerrarSesionComando(
            User.IdUsuario() ?? Guid.Empty, User.Jti() ?? string.Empty, User.ExpiraEn() ?? DateTime.UtcNow,
            solicitud?.RefreshToken, HttpContext.Ip());

        var resultado = await cerrarSesion.EjecutarAsync(comando, ct);
        return resultado.EsExito ? NoContent() : this.Problema(resultado.Error);
    }

    /// <summary>Datos del usuario autenticado.</summary>
    /// <response code="200">Usuario actual.</response>
    /// <response code="401">Sin token o con un token inválido.</response>
    /// <response code="404">El usuario del token ya no existe.</response>
    [HttpGet("yo")]
    [Authorize]
    [ProducesResponseType<UsuarioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UsuarioDto>> Yo([FromServices] ObtenerUsuarioActual obtenerUsuarioActual, CancellationToken ct)
    {
        var resultado = await obtenerUsuarioActual.EjecutarAsync(User.IdUsuario() ?? Guid.Empty, ct);
        return resultado.EsExito ? Ok(resultado.Valor) : this.Problema(resultado.Error);
    }
}
