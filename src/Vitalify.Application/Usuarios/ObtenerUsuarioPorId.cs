using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;

namespace Vitalify.Application.Usuarios;

/// <summary>Detalle de un usuario para el administrador (destino del <c>Location</c> al registrar).</summary>
public sealed class ObtenerUsuarioPorId(IRepositorioUsuarios usuarios)
{
    public async Task<Resultado<UsuarioDetalleDto>> EjecutarAsync(Guid id, CancellationToken ct = default)
    {
        var usuario = await usuarios.ObtenerPorIdAsync(id, ct);
        return usuario is null ? ErroresUsuario.NoEncontrado : UsuarioDetalleDto.Desde(usuario);
    }
}
