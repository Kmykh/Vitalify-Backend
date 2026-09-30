using Vitalify.Application.Camas;
using Vitalify.Domain.Camas;

namespace Vitalify.Application.Puertos;

public interface IRepositorioCamas
{
    Task<Cama?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default);

    /// <summary>Camas con su ocupación (derivada de las hospitalizaciones activas), sin datos del paciente.</summary>
    Task<IReadOnlyList<CamaDto>> ListarAsync(bool soloDisponibles, CancellationToken ct = default);

    void Agregar(Cama cama);
}
