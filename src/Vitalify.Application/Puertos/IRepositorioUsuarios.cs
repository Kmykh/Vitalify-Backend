using Vitalify.Application.Comun;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Puertos;

public interface IRepositorioUsuarios
{
    Task<Usuario?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    Task<Usuario?> ObtenerPorCorreoAsync(Correo correo, CancellationToken ct = default);

    Task<bool> ExisteCorreoAsync(Correo correo, CancellationToken ct = default);

    Task<bool> ExisteConRolAsync(Rol rol, CancellationToken ct = default);

    Task<Pagina<Usuario>> ListarAsync(int pagina, int tamano, CancellationToken ct = default);

    /// <summary>Usuarios activos con ese rol; con <paramref name="servicioId"/>, solo los de ese servicio.</summary>
    Task<IReadOnlyList<Usuario>> ListarActivosAsync(Rol rol, Guid? servicioId, CancellationToken ct = default);

    Task<IReadOnlyList<Usuario>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    void Agregar(Usuario usuario);
}
