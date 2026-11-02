using Vitalify.Application.Dispositivos;
using Vitalify.Domain.Dispositivos;

namespace Vitalify.Application.Puertos;

public interface IRepositorioDispositivos
{
    Task<Dispositivo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default);

    /// <summary>Por el código del tópico MQTT (ya normalizado a mayúsculas).</summary>
    Task<Dispositivo?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default);

    Task<IReadOnlyList<DispositivoDto>> ListarAsync(EstadoDispositivo? estado, CancellationToken ct = default);

    void Agregar(Dispositivo dispositivo);
}
