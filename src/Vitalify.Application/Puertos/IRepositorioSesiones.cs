using Vitalify.Domain.Sesiones;

namespace Vitalify.Application.Puertos;

public interface IRepositorioSesiones
{
    Task<SesionRefresco?> ObtenerPorHashAsync(string hashToken, CancellationToken ct = default);

    Task<IReadOnlyList<SesionRefresco>> ListarNoRevocadasAsync(Guid usuarioId, CancellationToken ct = default);

    void Agregar(SesionRefresco sesion);
}
