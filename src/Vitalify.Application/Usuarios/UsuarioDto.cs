using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Usuarios;

public sealed record UsuarioDto(Guid Id, string Nombre, string Correo, string Rol, Guid? ServicioId = null)
{
    public static UsuarioDto Desde(Usuario usuario) =>
        new(usuario.Id, usuario.Nombre, usuario.Correo.Valor, usuario.Rol.ToString(), usuario.ServicioId);
}

/// <param name="ServicioId">Servicio donde trabaja (null en el administrador).</param>
public sealed record UsuarioDetalleDto(Guid Id, string Nombre, string Correo, string Rol, bool Activo, DateTime CreadoEn, Guid? ServicioId = null)
{
    public static UsuarioDetalleDto Desde(Usuario usuario) =>
        new(usuario.Id, usuario.Nombre, usuario.Correo.Valor, usuario.Rol.ToString(), usuario.Activo, usuario.CreadoEn, usuario.ServicioId);
}
