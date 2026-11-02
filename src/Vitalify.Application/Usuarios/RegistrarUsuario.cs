using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Usuarios;

/// <param name="Rol">Solo <c>Medico</c> o <c>Enfermera</c>: los administradores no se crean por la API.</param>
/// <param name="AdministradorId">Quien registra, para la auditoría.</param>
/// <param name="ServicioId">Servicio donde trabaja: obligatorio para enfermeras y médicos.</param>
public sealed record RegistrarUsuarioComando(
    string Nombre, string Correo, string Contrasena, string Rol, Guid AdministradorId, string? Ip, Guid? ServicioId = null);

public sealed class RegistrarUsuarioValidador : AbstractValidator<RegistrarUsuarioComando>
{
    public static readonly IReadOnlyList<Rol> RolesRegistrables = [Domain.Usuarios.Rol.Medico, Domain.Usuarios.Rol.Enfermera];

    public RegistrarUsuarioValidador()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(c => c.Nombre)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nombre es obligatorio.")
            .Must(n => n.Trim().Length <= Usuario.LargoMaximoNombre)
            .WithMessage($"El nombre no puede superar {Usuario.LargoMaximoNombre} caracteres.");

        RuleFor(c => c.Correo)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("El correo es obligatorio.")
            .Must(Domain.Usuarios.Correo.EsValido).WithMessage("El correo no tiene un formato válido.");

        RuleFor(c => c.Contrasena)
            .Must(ReglasContrasena.EsValida).WithMessage(ReglasContrasena.Mensaje);

        RuleFor(c => c.Rol)
            .Must(r => TryParseRol(r, out _)).WithMessage("El rol debe ser Medico o Enfermera.");

        RuleFor(c => c.ServicioId)
            .Must(s => s is not null && s != Guid.Empty).WithMessage("El servicio es obligatorio para enfermeras y médicos (ver GET /servicios).");
    }

    /// <summary>Acepta solo los nombres de los roles registrables, sin distinguir mayúsculas (no números).</summary>
    public static bool TryParseRol(string? valor, out Rol rol)
    {
        rol = RolesRegistrables.FirstOrDefault(r => string.Equals(r.ToString(), valor?.Trim(), StringComparison.OrdinalIgnoreCase));
        return rol != default;
    }
}

/// <summary>HU01: el administrador registra una cuenta de médico o enfermera.</summary>
public sealed class RegistrarUsuario(
    IValidator<RegistrarUsuarioComando> validador,
    IRepositorioUsuarios usuarios,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IHasherContrasenas hasher,
    IRepositorioServicios servicios,
    IReloj reloj)
{
    public async Task<Resultado<UsuarioDetalleDto>> EjecutarAsync(RegistrarUsuarioComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var correo = Correo.Crear(comando.Correo);
        if (await usuarios.ExisteCorreoAsync(correo, ct))
        {
            return ErroresUsuario.CorreoEnUso;
        }

        if (await servicios.ObtenerPorIdAsync(comando.ServicioId!.Value, ct) is not { Activo: true })
        {
            return ErroresComunes.ValidacionDeCampo("servicioId", "El servicio no existe o no está activo.");
        }

        RegistrarUsuarioValidador.TryParseRol(comando.Rol, out var rol);
        var ahora = reloj.AhoraUtc;
        var usuario = Usuario.Crear(comando.Nombre, correo, hasher.Calcular(comando.Contrasena), rol, ahora, comando.ServicioId);

        usuarios.Agregar(usuario);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.UsuarioCreado, ahora, comando.AdministradorId, comando.Ip,
            $"Usuario {usuario.Id} creado con rol {rol}."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? UsuarioDetalleDto.Desde(usuario) : guardado.Error;
    }
}
