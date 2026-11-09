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

    public async Task<IReadOnlyList<ObservacionEnfermeria>> ListarAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        await db.Observaciones.AsNoTracking()
            .Where(o => o.HospitalizacionId == hospitalizacionId && o.ObservadaEn >= desde && o.ObservadaEn <= hasta)
            .OrderByDescending(o => o.ObservadaEn).ThenBy(o => o.Id)
            .ToListAsync(ct);

    public Task<ObservacionEnfermeria?> ObtenerUltimaTomaAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        db.Observaciones.AsNoTracking()
            .Where(o => o.HospitalizacionId == hospitalizacionId)
            .OrderByDescending(o => o.ObservadaEn)
            .FirstOrDefaultAsync(ct);

    public Task<BrazoPresion?> ObtenerUltimoBrazoPresionAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        db.Observaciones.AsNoTracking()
            .Where(o => o.HospitalizacionId == hospitalizacionId && o.BrazoPa != null)
            .OrderByDescending(o => o.ObservadaEn)
            .Select(o => o.BrazoPa)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, ObservacionEnfermeria>> ObtenerUltimasPreocupacionesAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default)
    {
        if (hospitalizaciones.Count == 0)
        {
            return new Dictionary<Guid, ObservacionEnfermeria>();
        }

        var ultimas = await db.Observaciones.AsNoTracking()
            .Where(o => hospitalizaciones.Contains(o.HospitalizacionId) && o.PreocupacionClinica == true)
            .GroupBy(o => o.HospitalizacionId)
            .Select(g => g.OrderByDescending(o => o.ObservadaEn).First())
            .ToListAsync(ct);
        return ultimas.ToDictionary(o => o.HospitalizacionId);
    }
}

internal sealed class RepositorioEvaluaciones(HistorialDbContext db) : IRepositorioEvaluaciones
{
    public void Agregar(EvaluacionRiesgo evaluacion) => db.Evaluaciones.Add(evaluacion);

    public Task<EvaluacionRiesgo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        db.Evaluaciones.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct);

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

    public async Task<IReadOnlyList<EvaluacionRiesgo>> ListarAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        await db.Evaluaciones.AsNoTracking()
            .Where(e => e.HospitalizacionId == hospitalizacionId && e.EvaluadaEn >= desde && e.EvaluadaEn <= hasta)
            .OrderBy(e => e.EvaluadaEn).ThenBy(e => e.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GrupoEvaluaciones>> AgruparAsync(
        Guid hospitalizacionId, DateTime desde, DateTime hasta, int segundos, CancellationToken ct = default)
    {
        string C(string propiedad) => ConsultasAgregadas.Columna<EvaluacionRiesgo>(db, propiedad);
        var evaluadaEn = C(nameof(EvaluacionRiesgo.EvaluadaEn));
        string Puntaje(string total, string rango) =>
            $"min({C(total)}), max({C(total)}), (array_agg({C(total)} ORDER BY {evaluadaEn} DESC))[1], max({rango})";
        var sql = $"""
            SELECT {ConsultasAgregadas.InicioDeGrupo(evaluadaEn)} AS inicio, count(*)::int,
                   (count(*) FILTER (WHERE {C(nameof(EvaluacionRiesgo.News2Completo))} AND {C(nameof(EvaluacionRiesgo.MewsCompleto))}))::int,
                   {Puntaje(nameof(EvaluacionRiesgo.News2Total), ConsultasAgregadas.RangoDeEnum<NivelRiesgoNews2>(C(nameof(EvaluacionRiesgo.News2Nivel))))},
                   {Puntaje(nameof(EvaluacionRiesgo.MewsTotal), ConsultasAgregadas.RangoDeEnum<NivelRiesgoMews>(C(nameof(EvaluacionRiesgo.MewsNivel))))}
            FROM {ConsultasAgregadas.Tabla<EvaluacionRiesgo>(db)}
            WHERE {C(nameof(EvaluacionRiesgo.HospitalizacionId))} = @hospitalizacion
              AND {evaluadaEn} >= @desde AND {evaluadaEn} <= @hasta
            GROUP BY 1
            ORDER BY 1
            """;
        return await ConsultasAgregadas.LeerAsync(db, sql,
            l => new GrupoEvaluaciones(
                l.GetDateTime(0), l.GetInt32(1), l.GetInt32(2),
                l.GetInt32(3), l.GetInt32(4), l.GetInt32(5), (NivelRiesgoNews2)l.GetInt32(6),
                l.GetInt32(7), l.GetInt32(8), l.GetInt32(9), (NivelRiesgoMews)l.GetInt32(10)),
            ct,
            new Npgsql.NpgsqlParameter("hospitalizacion", hospitalizacionId), new Npgsql.NpgsqlParameter("desde", desde),
            new Npgsql.NpgsqlParameter("hasta", hasta), new Npgsql.NpgsqlParameter("segundos", segundos));
    }

    public async Task<IReadOnlyDictionary<Guid, TendenciaRiesgo>> ObtenerTendenciasAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        if (hospitalizaciones.Count == 0)
        {
            return new Dictionary<Guid, TendenciaRiesgo>();
        }

        // Todo se agrega en SQL: en una guardia de 12 h hay hasta ~4300 evaluaciones por paciente.
        var filas = await db.Evaluaciones.AsNoTracking()
            .Where(e => hospitalizaciones.Contains(e.HospitalizacionId) && e.EvaluadaEn >= desde && e.EvaluadaEn <= hasta)
            .GroupBy(e => e.HospitalizacionId)
            .Select(g => new
            {
                Hospitalizacion = g.Key,
                Cantidad = g.Count(),
                News2Maximo = g.Max(e => e.News2Total),
                MewsMaximo = g.Max(e => e.MewsTotal),
                Primera = g.OrderBy(e => e.EvaluadaEn).Select(e => new { e.News2Total, e.MewsTotal }).First(),
                Ultima = g.OrderByDescending(e => e.EvaluadaEn).Select(e => new { e.News2Total, e.MewsTotal }).First(),
            })
            .ToListAsync(ct);

        return filas.ToDictionary(f => f.Hospitalizacion, f => new TendenciaRiesgo(
            new TendenciaPuntaje(f.Primera.News2Total, f.News2Maximo, f.Ultima.News2Total),
            new TendenciaPuntaje(f.Primera.MewsTotal, f.MewsMaximo, f.Ultima.MewsTotal),
            f.Cantidad));
    }
}
