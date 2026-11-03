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

    public async Task<IReadOnlyList<GrupoLecturas>> AgruparAsync(
        Guid hospitalizacionId, DateTime desde, DateTime hasta, int segundos, CancellationToken ct = default)
    {
        string C(string propiedad) => ConsultasAgregadas.Columna<LecturaSignos>(db, propiedad);
        // El orden de las variables es el de GrupoLecturas: tres columnas (promedio, mínimo, máximo) por variable.
        var variables = new[] { nameof(LecturaSignos.Fc), nameof(LecturaSignos.Fr), nameof(LecturaSignos.Spo2), nameof(LecturaSignos.Temperatura), nameof(LecturaSignos.Pas), nameof(LecturaSignos.Pad) }
            .Select(v => $"round(avg({C(v)})::numeric, 1), min({C(v)})::numeric, max({C(v)})::numeric");
        var sql = $"""
            SELECT {ConsultasAgregadas.InicioDeGrupo(C(nameof(LecturaSignos.MedidoEn)))} AS inicio, count(*)::int,
                   {string.Join(", ", variables)}, bool_or({C(nameof(LecturaSignos.Caida))})
            FROM {ConsultasAgregadas.Tabla<LecturaSignos>(db)}
            WHERE {C(nameof(LecturaSignos.HospitalizacionId))} = @hospitalizacion
              AND {C(nameof(LecturaSignos.MedidoEn))} >= @desde AND {C(nameof(LecturaSignos.MedidoEn))} <= @hasta
            GROUP BY 1
            ORDER BY 1
            """;
        return await ConsultasAgregadas.LeerAsync(db, sql,
            l => new GrupoLecturas(
                l.GetDateTime(0), l.GetInt32(1),
                ConsultasAgregadas.Estadistica(l, 2), ConsultasAgregadas.Estadistica(l, 5), ConsultasAgregadas.Estadistica(l, 8),
                ConsultasAgregadas.Estadistica(l, 11), ConsultasAgregadas.Estadistica(l, 14), ConsultasAgregadas.Estadistica(l, 17),
                l.GetBoolean(20)),
            ct,
            new Npgsql.NpgsqlParameter("hospitalizacion", hospitalizacionId), new Npgsql.NpgsqlParameter("desde", desde),
            new Npgsql.NpgsqlParameter("hasta", hasta), new Npgsql.NpgsqlParameter("segundos", segundos));
    }

    public async Task<ExtremosSignos> ObtenerExtremosAsync(Guid hospitalizacionId, CancellationToken ct = default)
    {
        string C(string propiedad) => ConsultasAgregadas.Columna<LecturaSignos>(db, propiedad);
        // El orden de las variables es el de ExtremosSignos: tres columnas (mínimo, máximo, cantidad) por variable.
        var variables = new[] { nameof(LecturaSignos.Fc), nameof(LecturaSignos.Fr), nameof(LecturaSignos.Spo2), nameof(LecturaSignos.Temperatura), nameof(LecturaSignos.Pas), nameof(LecturaSignos.Pad) }
            .Select(v => $"min({C(v)})::numeric, max({C(v)})::numeric, count({C(v)})::int");
        var sql = $"""
            SELECT {string.Join(", ", variables)}
            FROM {ConsultasAgregadas.Tabla<LecturaSignos>(db)}
            WHERE {C(nameof(LecturaSignos.HospitalizacionId))} = @hospitalizacion
            """;
        var filas = await ConsultasAgregadas.LeerAsync(db, sql,
            l => new ExtremosSignos(
                ConsultasAgregadas.Rango(l, 0), ConsultasAgregadas.Rango(l, 3), ConsultasAgregadas.Rango(l, 6),
                ConsultasAgregadas.Rango(l, 9), ConsultasAgregadas.Rango(l, 12), ConsultasAgregadas.Rango(l, 15)),
            ct,
            new Npgsql.NpgsqlParameter("hospitalizacion", hospitalizacionId));
        return filas.Single();
    }

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
