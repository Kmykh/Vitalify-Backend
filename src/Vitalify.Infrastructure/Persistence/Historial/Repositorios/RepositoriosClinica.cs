using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Clinica;

namespace Vitalify.Infrastructure.Persistence.Historial.Repositorios;

internal sealed class RepositorioObservaciones(HistorialDbContext db) : IRepositorioObservaciones
{
    public void Agregar(ObservacionEnfermeria observacion) => db.Observaciones.Add(observacion);

    public async Task<IReadOnlyList<ObservacionEnfermeria>> ListarDesdeAsync(Guid hospitalizacionId, DateTime desde, CancellationToken ct = default)
    {
        var guardadas = await db.Observaciones.AsNoTracking()
            .Where(o => o.HospitalizacionId == hospitalizacionId && o.ObservadaEn >= desde)
            .ToListAsync(ct);

        // Incluye las agregadas en esta unidad de trabajo que aún no se guardan.
        var pendientes = db.ChangeTracker.Entries<ObservacionEnfermeria>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(o => o.HospitalizacionId == hospitalizacionId && o.ObservadaEn >= desde);

        return guardadas.Concat(pendientes).OrderByDescending(o => o.ObservadaEn).ToList();
    }
}

internal sealed class RepositorioEvaluaciones(HistorialDbContext db) : IRepositorioEvaluaciones
{
    public void Agregar(EvaluacionRiesgo evaluacion) => db.Evaluaciones.Add(evaluacion);

    public Task<EvaluacionRiesgo?> ObtenerUltimaAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        db.Evaluaciones.AsNoTracking()
            .Where(e => e.HospitalizacionId == hospitalizacionId)
            .OrderByDescending(e => e.EvaluadaEn)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, EvaluacionRiesgo>> ObtenerUltimasAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default)
    {
        if (hospitalizaciones.Count == 0)
        {
            return new Dictionary<Guid, EvaluacionRiesgo>();
        }

        var ultimas = await db.Evaluaciones.AsNoTracking()
            .Where(e => hospitalizaciones.Contains(e.HospitalizacionId))
            .GroupBy(e => e.HospitalizacionId)
            .Select(g => g.OrderByDescending(e => e.EvaluadaEn).First())
            .ToListAsync(ct);

        return ultimas.ToDictionary(e => e.HospitalizacionId);
    }
}
