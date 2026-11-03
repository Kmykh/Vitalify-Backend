using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Historial;
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

    /// <summary>Las variables en el orden de <see cref="GrupoLecturas"/> y <see cref="ExtremosSignos"/>.</summary>
    private static readonly string[] Variables = ["Fc", "Fr", "Spo2", "Temperatura", "Pas", "Pad"];

    public async Task<IReadOnlyList<GrupoLecturas>> AgruparAsync(
        Guid hospitalizacionId, DateTime desde, DateTime hasta, int segundos, CancellationToken ct = default)
    {
        string C(string propiedad) => ConsultasAgregadas.Columna<LecturaSignos>(db, propiedad);
        string R(string propiedad) => ConsultasAgregadas.Columna<LecturaSignosResumen>(db, propiedad);

        // Cada fuente da, por grupo y variable: suma, mediciones, mínimo y máximo. Así el promedio final es el ponderado
        // aunque un grupo de 15 min o 1 h junte resúmenes de 5 min. Las crudas y las resumidas nunca se solapan: la
        // retención borra las crudas en la misma sentencia que las resume.
        var crudas = string.Join(", ", Variables.Select(v =>
            $"sum({C(v)})::numeric AS {v}_suma, count({C(v)})::bigint AS {v}_n, min({C(v)})::numeric AS {v}_min, max({C(v)})::numeric AS {v}_max"));
        var resumidas = string.Join(", ", Variables.Select(v =>
            $"sum({R(v + "Promedio")} * {R(v + "Mediciones")}) AS {v}_suma, sum({R(v + "Mediciones")})::bigint AS {v}_n, " +
            $"min({R(v + "Minimo")})::numeric AS {v}_min, max({R(v + "Maximo")})::numeric AS {v}_max"));
        // El orden de las columnas finales es el de GrupoLecturas: promedio, mínimo y máximo por variable.
        var finales = string.Join(", ", Variables.Select(v =>
            $"round(sum({v}_suma) / nullif(sum({v}_n), 0), 1), min({v}_min), max({v}_max)"));
        var sql = $"""
            WITH crudas AS (
                SELECT {ConsultasAgregadas.InicioDeGrupo(C(nameof(LecturaSignos.MedidoEn)))} AS inicio, count(*)::bigint AS lecturas,
                       {crudas}, bool_or({C(nameof(LecturaSignos.Caida))}) AS caida, false AS resumido
                FROM {ConsultasAgregadas.Tabla<LecturaSignos>(db)}
                WHERE {C(nameof(LecturaSignos.HospitalizacionId))} = @hospitalizacion
                  AND {C(nameof(LecturaSignos.MedidoEn))} >= @desde AND {C(nameof(LecturaSignos.MedidoEn))} <= @hasta
                GROUP BY 1
            ), resumidas AS (
                SELECT {ConsultasAgregadas.InicioDeGrupo(R(nameof(LecturaSignosResumen.Inicio)))} AS inicio,
                       sum({R(nameof(LecturaSignosResumen.Lecturas))})::bigint AS lecturas,
                       {resumidas}, bool_or({R(nameof(LecturaSignosResumen.Caida))}) AS caida, true AS resumido
                FROM {ConsultasAgregadas.Tabla<LecturaSignosResumen>(db)}
                WHERE {R(nameof(LecturaSignosResumen.HospitalizacionId))} = @hospitalizacion
                  AND {R(nameof(LecturaSignosResumen.Inicio))} > @desdeResumen AND {R(nameof(LecturaSignosResumen.Inicio))} <= @hasta
                GROUP BY 1
            )
            SELECT inicio, sum(lecturas)::int, {finales}, bool_or(caida), bool_or(resumido)
            FROM (SELECT * FROM crudas UNION ALL SELECT * FROM resumidas) AS fuentes
            GROUP BY inicio
            ORDER BY inicio
            """;
        return await ConsultasAgregadas.LeerAsync(db, sql,
            l => new GrupoLecturas(
                l.GetDateTime(0), l.GetInt32(1),
                ConsultasAgregadas.Estadistica(l, 2), ConsultasAgregadas.Estadistica(l, 5), ConsultasAgregadas.Estadistica(l, 8),
                ConsultasAgregadas.Estadistica(l, 11), ConsultasAgregadas.Estadistica(l, 14), ConsultasAgregadas.Estadistica(l, 17),
                l.GetBoolean(20), l.GetBoolean(21)),
            ct,
            new Npgsql.NpgsqlParameter("hospitalizacion", hospitalizacionId), new Npgsql.NpgsqlParameter("desde", desde),
            new Npgsql.NpgsqlParameter("hasta", hasta), new Npgsql.NpgsqlParameter("segundos", segundos),
            // Un resumen de 5 min que empezó antes de "desde" pero termina después también entra.
            new Npgsql.NpgsqlParameter("desdeResumen", desde - PoliticaRetencion.IntervaloResumen));
    }

    public async Task<ExtremosSignos> ObtenerExtremosAsync(Guid hospitalizacionId, CancellationToken ct = default)
    {
        string C(string propiedad) => ConsultasAgregadas.Columna<LecturaSignos>(db, propiedad);
        string R(string propiedad) => ConsultasAgregadas.Columna<LecturaSignosResumen>(db, propiedad);
        var crudas = string.Join(", ", Variables.Select(v => $"min({C(v)})::numeric AS {v}_min, max({C(v)})::numeric AS {v}_max, count({C(v)})::bigint AS {v}_n"));
        var resumidas = string.Join(", ", Variables.Select(v =>
            $"min({R(v + "Minimo")})::numeric, max({R(v + "Maximo")})::numeric, coalesce(sum({R(v + "Mediciones")}), 0)::bigint"));
        // El orden de las columnas finales es el de ExtremosSignos: mínimo, máximo y cantidad por variable.
        var finales = string.Join(", ", Variables.Select(v => $"min({v}_min), max({v}_max), sum({v}_n)::int"));
        var sql = $"""
            SELECT {finales}
            FROM (
                SELECT {crudas}
                FROM {ConsultasAgregadas.Tabla<LecturaSignos>(db)}
                WHERE {C(nameof(LecturaSignos.HospitalizacionId))} = @hospitalizacion
                UNION ALL
                SELECT {resumidas}
                FROM {ConsultasAgregadas.Tabla<LecturaSignosResumen>(db)}
                WHERE {R(nameof(LecturaSignosResumen.HospitalizacionId))} = @hospitalizacion
            ) AS fuentes
            """;
        var filas = await ConsultasAgregadas.LeerAsync(db, sql,
            l => new ExtremosSignos(
                ConsultasAgregadas.Rango(l, 0), ConsultasAgregadas.Rango(l, 3), ConsultasAgregadas.Rango(l, 6),
                ConsultasAgregadas.Rango(l, 9), ConsultasAgregadas.Rango(l, 12), ConsultasAgregadas.Rango(l, 15)),
            ct,
            new Npgsql.NpgsqlParameter("hospitalizacion", hospitalizacionId));
        return filas.Single();
    }

    public async Task<DateTime?> ObtenerResumidoHastaAsync(Guid hospitalizacionId, CancellationToken ct = default)
    {
        var ultimo = await db.ResumenesLecturas.AsNoTracking()
            .Where(r => r.HospitalizacionId == hospitalizacionId)
            .MaxAsync(r => (DateTime?)r.Inicio, ct);
        return ultimo + PoliticaRetencion.IntervaloResumen;
    }

    public async Task<IReadOnlyList<Guid>> ListarHospitalizacionesConLecturasCrudasAsync(DateTime antesDe, CancellationToken ct = default) =>
        await db.Lecturas.AsNoTracking()
            .Where(l => l.MedidoEn < antesDe)
            .Select(l => l.HospitalizacionId)
            .Distinct()
            .ToListAsync(ct);

    public async Task<LecturasResumidas> ResumirAsync(Guid hospitalizacionId, DateTime resumidoEn, CancellationToken ct = default)
    {
        var (lecturas, grupos) = (0, 0);

        // Un día por sentencia, para no tener una transacción enorme con un episodio largo. Los días UTC son múltiplos de
        // 5 min: ningún grupo queda partido entre dos tramos (y si quedara, ON CONFLICT lo combina).
        while (await db.Lecturas.AsNoTracking()
                   .Where(l => l.HospitalizacionId == hospitalizacionId)
                   .MinAsync(l => (DateTime?)l.MedidoEn, ct) is { } primera)
        {
            var desde = new DateTime(primera.Year, primera.Month, primera.Day, 0, 0, 0, DateTimeKind.Utc);
            var (borradas, escritos) = await ResumirTramoAsync(hospitalizacionId, desde, desde.AddDays(1), resumidoEn, ct);
            if (borradas == 0)
            {
                throw new InvalidOperationException($"La retención no avanzó en la hospitalización {hospitalizacionId}.");
            }

            lecturas += borradas;
            grupos += escritos;
        }

        return new LecturasResumidas(lecturas, grupos);
    }

    /// <summary>
    /// Borra las crudas del tramo y escribe sus grupos de 5 min en la misma sentencia (CTE con <c>DELETE ... RETURNING</c>):
    /// o pasan las dos cosas o ninguna. Si el grupo ya existía, lo combina con el promedio ponderado.
    /// </summary>
    private async Task<(int Lecturas, int Grupos)> ResumirTramoAsync(
        Guid hospitalizacionId, DateTime desde, DateTime hasta, DateTime resumidoEn, CancellationToken ct)
    {
        string C(string propiedad) => ConsultasAgregadas.Columna<LecturaSignos>(db, propiedad);
        string R(string propiedad) => ConsultasAgregadas.Columna<LecturaSignosResumen>(db, propiedad);
        string P(string variable, string sufijo) => R(variable + sufijo);

        var columnas = new List<string>
        {
            R(nameof(LecturaSignosResumen.HospitalizacionId)), R(nameof(LecturaSignosResumen.Inicio)), R(nameof(LecturaSignosResumen.PacienteId)),
            R(nameof(LecturaSignosResumen.Lecturas)), R(nameof(LecturaSignosResumen.Caida)), R(nameof(LecturaSignosResumen.ResumidoEn)),
        };
        var valores = new List<string> { "@hospitalizacion", "inicio", "paciente_id", "lecturas", "caida", "@resumidoEn" };
        var agregados = new List<string>();
        var combinar = new List<string>
        {
            $"{R(nameof(LecturaSignosResumen.Lecturas))} = r.{R(nameof(LecturaSignosResumen.Lecturas))} + EXCLUDED.{R(nameof(LecturaSignosResumen.Lecturas))}",
            $"{R(nameof(LecturaSignosResumen.Caida))} = r.{R(nameof(LecturaSignosResumen.Caida))} OR EXCLUDED.{R(nameof(LecturaSignosResumen.Caida))}",
            $"{R(nameof(LecturaSignosResumen.ResumidoEn))} = EXCLUDED.{R(nameof(LecturaSignosResumen.ResumidoEn))}",
        };
        foreach (var v in Variables)
        {
            var (promedio, minimo, maximo, mediciones) = (P(v, "Promedio"), P(v, "Minimo"), P(v, "Maximo"), P(v, "Mediciones"));
            columnas.AddRange([promedio, minimo, maximo, mediciones]);
            valores.AddRange([$"{v}_promedio", $"{v}_minimo", $"{v}_maximo", $"{v}_mediciones"]);
            agregados.Add($"avg({C(v)}) AS {v}_promedio, min({C(v)}) AS {v}_minimo, max({C(v)}) AS {v}_maximo, count({C(v)})::int AS {v}_mediciones");
            combinar.Add(
                $"{promedio} = CASE WHEN r.{mediciones} + EXCLUDED.{mediciones} = 0 THEN NULL ELSE " +
                $"(coalesce(r.{promedio} * r.{mediciones}, 0) + coalesce(EXCLUDED.{promedio} * EXCLUDED.{mediciones}, 0)) / (r.{mediciones} + EXCLUDED.{mediciones}) END");
            combinar.Add($"{minimo} = least(r.{minimo}, EXCLUDED.{minimo})");
            combinar.Add($"{maximo} = greatest(r.{maximo}, EXCLUDED.{maximo})");
            combinar.Add($"{mediciones} = r.{mediciones} + EXCLUDED.{mediciones}");
        }

        var tablaResumen = ConsultasAgregadas.Tabla<LecturaSignosResumen>(db);
        var sql = $"""
            WITH borradas AS (
                DELETE FROM {ConsultasAgregadas.Tabla<LecturaSignos>(db)}
                WHERE {C(nameof(LecturaSignos.HospitalizacionId))} = @hospitalizacion
                  AND {C(nameof(LecturaSignos.MedidoEn))} >= @desde AND {C(nameof(LecturaSignos.MedidoEn))} < @hasta
                RETURNING *
            ), grupos AS (
                SELECT {ConsultasAgregadas.InicioDeGrupo(C(nameof(LecturaSignos.MedidoEn)))} AS inicio,
                       (array_agg({C(nameof(LecturaSignos.PacienteId))}))[1] AS paciente_id,
                       count(*)::int AS lecturas, bool_or({C(nameof(LecturaSignos.Caida))}) AS caida,
                       {string.Join(", ", agregados)}
                FROM borradas
                GROUP BY 1
            ), escritos AS (
                INSERT INTO {tablaResumen} AS r ({string.Join(", ", columnas)})
                SELECT {string.Join(", ", valores)} FROM grupos
                ON CONFLICT ({R(nameof(LecturaSignosResumen.HospitalizacionId))}, {R(nameof(LecturaSignosResumen.Inicio))}) DO UPDATE SET
                    {string.Join(", ", combinar)}
                RETURNING 1
            )
            SELECT (SELECT count(*) FROM borradas)::int, (SELECT count(*) FROM escritos)::int
            """;
        var filas = await ConsultasAgregadas.LeerAsync(db, sql, l => (l.GetInt32(0), l.GetInt32(1)), ct,
            new Npgsql.NpgsqlParameter("hospitalizacion", hospitalizacionId), new Npgsql.NpgsqlParameter("desde", desde),
            new Npgsql.NpgsqlParameter("hasta", hasta), new Npgsql.NpgsqlParameter("resumidoEn", resumidoEn),
            new Npgsql.NpgsqlParameter("segundos", (int)PoliticaRetencion.IntervaloResumen.TotalSeconds));
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
