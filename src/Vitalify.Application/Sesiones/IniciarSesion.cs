using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Usuarios;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Sesiones;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Sesiones;

public sealed record IniciarSesionComando(string Correo, string Contrasena, string? Ip);

public sealed class IniciarSesionValidador : AbstractValidator<IniciarSesionComando>
{
    public IniciarSesionValidador()
    {
        RuleFor(c => c.Correo).Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("El correo es obligatorio.");
        RuleFor(c => c.Contrasena).Must(c => !string.IsNullOrEmpty(c)).WithMessage("La contraseña es obligatoria.");
    }
}

/// <summary>HU02: inicio de sesión con correo y contraseña. Emite el access token (JWT) y un refresh token.</summary>
public sealed class IniciarSesion(
    IValidator<IniciarSesionComando> validador,
    IRepositorioUsuarios usuarios,
    IRepositorioSesiones sesiones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IHasherContrasenas hasher,
    IGeneradorTokens generador,
    IReloj reloj,
    OpcionesSesion opciones)
{
    public async Task<Resultado<SesionIniciada>> EjecutarAsync(IniciarSesionComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var ahora = reloj.AhoraUtc;
        var usuario = Correo.TryCrear(comando.Correo, out var correo)
            ? await usuarios.ObtenerPorCorreoAsync(correo, ct)
            : null;

        // Se verifica siempre (con un hash ficticio si el usuario no existe) para no revelar por el tiempo
        // de respuesta qué correos están registrados.
        var contrasenaCorrecta = hasher.Verificar(usuario?.HashContrasena, comando.Contrasena);

        if (usuario is null || !contrasenaCorrecta || !usuario.Activo)
        {
            var motivo = usuario is null ? "correo no registrado"
                : !contrasenaCorrecta ? "contraseña incorrecta"
                : "usuario inactivo";
            var correoIntentado = correo?.Valor ?? comando.Correo.Trim();
            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.LoginFallido, ahora, usuario?.Id, comando.Ip,
                $"Correo: {correoIntentado[..Math.Min(correoIntentado.Length, Correo.LargoMaximo)]}. Motivo: {motivo}."));
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
            return ErroresSesion.CredencialesInvalidas;
        }

        var refreshToken = generador.GenerarRefreshToken();
        sesiones.Agregar(SesionRefresco.Iniciar(usuario.Id, generador.CalcularHash(refreshToken), ahora, opciones.DuracionMaxima));
        var accessToken = generador.GenerarAccessToken(usuario, ahora);
        auditoria.Agregar(RegistroAuditoria.Registrar(AccionAuditoria.LoginExitoso, ahora, usuario.Id, comando.Ip));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        return new SesionIniciada(accessToken.Token, accessToken.ExpiraEn, refreshToken, UsuarioDto.Desde(usuario));
    }
}
