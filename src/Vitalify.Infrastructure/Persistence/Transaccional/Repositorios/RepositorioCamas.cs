using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Camas;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Camas;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioCamas(TransaccionalDbContext db) : IRepositorioCamas
{
    public async Task<Cama?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) => await db.Camas.FindAsync([id], ct);

    public Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default) => db.Camas.AnyAsync(c => c.Codigo == codigo, ct);

    public async Task<IReadOnlyList<CamaDto>> ListarAsync(bool soloDisponibles, CancellationToken ct = default)
    {
        var camas = db.Camas.AsNoTracking();
        if (soloDisponibles)
        {
            camas = camas.Where(c => c.Activa && !db.Hospitalizaciones.Any(h => h.CamaId == c.Id && h.Estado == EstadoHospitalizacion.Activa));
        }

        return await camas
            .OrderBy(c => c.Codigo)
            .Select(c => new CamaDto(c.Id, c.Codigo, c.Servicio, c.Activa,
                db.Hospitalizaciones.Any(h => h.CamaId == c.Id && h.Estado == EstadoHospitalizacion.Activa)))
            .ToListAsync(ct);
    }

    public void Agregar(Cama cama) => db.Camas.Add(cama);
}
