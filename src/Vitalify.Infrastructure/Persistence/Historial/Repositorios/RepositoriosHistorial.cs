using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Infrastructure.Persistence.Historial.Repositorios;

internal sealed class RepositorioLecturas(HistorialDbContext db) : IRepositorioLecturas
{
    public Task<bool> ExisteAsync(string codigoDispositivo, DateTime medidoEn, CancellationToken ct = default) =>
        db.Lecturas.AnyAsync(l => l.CodigoDispositivo == codigoDispositivo && l.MedidoEn == medidoEn, ct);

    public async Task<IReadOnlyList<LecturaSignos>> ListarPorHospitalizacionAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        await db.Lecturas.AsNoTracking()
            .Where(l => l.HospitalizacionId == hospitalizacionId && l.MedidoEn >= desde && l.MedidoEn <= hasta)
            .OrderBy(l => l.MedidoEn)
            .ToListAsync(ct);

    public void Agregar(LecturaSignos lectura) => db.Lecturas.Add(lectura);
}

internal sealed class RepositorioEstadoSignos(HistorialDbContext db) : IRepositorioEstadoSignos
{
    public Task<EstadoSignosActual?> ObtenerAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        db.EstadosSignos.SingleOrDefaultAsync(e => e.HospitalizacionId == hospitalizacionId, ct);

    public async Task<IReadOnlyDictionary<Guid, EstadoSignosActual>> ObtenerVariosAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default)
    {
        if (hospitalizaciones.Count == 0)
        {
            return new Dictionary<Guid, EstadoSignosActual>();
        }

        return await db.EstadosSignos.AsNoTracking()
            .Where(e => hospitalizaciones.Contains(e.HospitalizacionId))
            .ToDictionaryAsync(e => e.HospitalizacionId, ct);
    }

    public void Agregar(EstadoSignosActual estado) => db.EstadosSignos.Add(estado);
}

internal sealed class RepositorioIncidencias(HistorialDbContext db) : IRepositorioIncidencias
{
    public void Agregar(IncidenciaTelemetria incidencia) => db.Incidencias.Add(incidencia);

    public async Task<Pagina<IncidenciaTelemetria>> ListarAsync(FiltroIncidencias filtro, int pagina, int tamano, CancellationToken ct = default)
    {
        var consulta = db.Incidencias.AsNoTracking();
        if (filtro.Desde is { } desde)
        {
            consulta = consulta.Where(i => i.OcurridaEn >= desde);
        }

        if (filtro.Hasta is { } hasta)
        {
            consulta = consulta.Where(i => i.OcurridaEn <= hasta);
        }

        if (filtro.CodigoDispositivo is { } codigo)
        {
            consulta = consulta.Where(i => i.CodigoDispositivo == codigo);
        }

        if (filtro.Tipo is { } tipo)
        {
            consulta = consulta.Where(i => i.Tipo == tipo);
        }

        var total = await consulta.CountAsync(ct);
        var elementos = await consulta
            .OrderByDescending(i => i.OcurridaEn).ThenBy(i => i.Id)
            .Skip((pagina - 1) * tamano).Take(tamano)
            .ToListAsync(ct);

        return new Pagina<IncidenciaTelemetria>(elementos, pagina, tamano, total);
    }
}
