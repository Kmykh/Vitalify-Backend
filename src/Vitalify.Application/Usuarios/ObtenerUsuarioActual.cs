using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;

namespace Vitalify.Application.Usuarios;

/// <summary>Datos del usuario autenticado (<c>GET /auth/yo</c>).</summary>
public sealed class ObtenerUsuarioActual(IRepositorioUsuarios usuarios)
{
    public async Task<Resultado<UsuarioDto>> EjecutarAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var usuario = await usuarios.ObtenerPorIdAsync(usuarioId, ct);
        return usuario is null ? ErroresUsuario.NoEncontrado : UsuarioDto.Desde(usuario);
    }
}
