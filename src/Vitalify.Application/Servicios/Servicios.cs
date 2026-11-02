using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Usuarios;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Servicios;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Servicios;

public sealed record ServicioDto(Guid Id, string Codigo, string Nombre, bool Activo)
{
    public static ServicioDto Desde(Servicio s) => new(s.Id, s.Codigo, s.Nombre, s.Activo);
}

public static class ErroresServicio
{
    public static readonly Error NoEncontrado = Error.NoEncontrado("servicio-no-encontrado", "El servicio no existe.");

    public static readonly Error CodigoDuplicado = Error.Conflicto("servicio-codigo-duplicado", "Ya existe un servicio con ese código.");

    public static Error Inexistente(string campo) => ErroresComunes.ValidacionDeCampo(campo, "El servicio no existe o no está activo.");
}

public sealed record RegistrarServicioComando(string Codigo, string Nombre);

public sealed class RegistrarServicioValidador : AbstractValidator<RegistrarServicioComando>
{
    public RegistrarServicioValidador()
    {
        RuleFor(c => c.Codigo).Must(Servicio.CodigoValido)
            .WithMessage("El código debe tener de 2 a 30 letras, números o guiones (por ejemplo EMERGENCIA o UVI).");
        RuleFor(c => c.Nombre).Must(n => n?.Trim().Length is > 0 and <= Servicio.LargoMaximoNombre)
            .WithMessage($"El nombre es obligatorio y no puede superar {Servicio.LargoMaximoNombre} caracteres.");
    }
}

public sealed class RegistrarServicio(IValidator<RegistrarServicioComando> validador, IRepositorioServicios servicios, IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<Resultado<ServicioDto>> EjecutarAsync(RegistrarServicioComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var servicio = Servicio.Registrar(comando.Codigo, comando.Nombre);
        if (await servicios.ExisteCodigoAsync(servicio.Codigo, ct))
        {
            return ErroresServicio.CodigoDuplicado;
        }

        servicios.Agregar(servicio);
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? ServicioDto.Desde(servicio) : guardado.Error;
    }
}

public sealed class ListarServicios(IRepositorioServicios servicios)
{
    public async Task<IReadOnlyList<ServicioDto>> EjecutarAsync(CancellationToken ct = default) =>
        (await servicios.ListarAsync(ct)).Select(ServicioDto.Desde).ToList();
}

public sealed record AsignarServicioComando(Guid UsuarioId, Guid? ServicioId, Guid AdministradorId, string? Ip);

/// <summary>El administrador asigna el servicio de una enfermera o un médico (define sus turnos y sus alertas).</summary>
public sealed class AsignarServicio(
    IRepositorioUsuarios usuarios, IRepositorioServicios servicios, IRepositorioAuditoria auditoria, IUnidadDeTrabajo unidadDeTrabajo, IReloj reloj)
{
    public async Task<Resultado<UsuarioDetalleDto>> EjecutarAsync(AsignarServicioComando comando, CancellationToken ct = default)
    {
        if (comando.ServicioId is not { } servicioId || await servicios.ObtenerPorIdAsync(servicioId, ct) is not { Activo: true })
        {
            return ErroresServicio.Inexistente("servicioId");
        }

        var usuario = await usuarios.ObtenerPorIdAsync(comando.UsuarioId, ct);
        if (usuario is null)
        {
            return ErroresUsuario.NoEncontrado;
        }

        if (usuario.Rol == Rol.Administrador)
        {
            return ErroresComunes.ValidacionDeCampo("usuarioId", "El administrador no pertenece a un servicio clínico.");
        }

        var ahora = reloj.AhoraUtc;
        usuario.AsignarServicio(servicioId, ahora);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.ServicioAsignado, ahora, comando.AdministradorId, comando.Ip, $"Usuario {usuario.Id}, servicio {servicioId}."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? UsuarioDetalleDto.Desde(usuario) : guardado.Error;
    }
}
