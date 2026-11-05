using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vitalify.Application.Puertos;
using Vitalify.Infrastructure.Persistence.Historial;
using Vitalify.Infrastructure.Persistence.Historial.Repositorios;
using Vitalify.Infrastructure.Persistence.Transaccional;

namespace Vitalify.Infrastructure.Persistence;

/// <summary>
/// Tamaños desde el catálogo de PostgreSQL (<c>pg_class</c>): cada contexto mide su propio esquema por su propia
/// conexión, así que sigue funcionando si el historial pasa a otra base. No lee filas de ninguna tabla.
/// </summary>
internal sealed class MedidorAlmacenamiento(TransaccionalDbContext transaccional, HistorialDbContext historial) : IMedidorAlmacenamiento
{
    private const string SqlTablas = """
        SELECT c.relname, pg_total_relation_size(c.oid), pg_indexes_size(c.oid), c.reltuples::bigint
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = @esquema AND c.relkind IN ('r', 'p')
        """;

    public async Task<long> MedirBaseAsync(CancellationToken ct = default) =>
        (await ConsultasAgregadas.LeerAsync(transaccional, "SELECT pg_database_size(current_database())", l => l.GetInt64(0), ct)).Single();

    public async Task<IReadOnlyList<TamanoTabla>> MedirTablasAsync(CancellationToken ct = default) =>
    [
        .. await MedirAsync(transaccional, "transaccional", TransaccionalDbContext.Esquema, ct),
        .. await MedirAsync(historial, "historial", HistorialDbContext.Esquema, ct),
    ];

    private static Task<List<TamanoTabla>> MedirAsync(DbContext db, string contexto, string esquema, CancellationToken ct) =>
        ConsultasAgregadas.LeerAsync(db, SqlTablas,
            l => new TamanoTabla(contexto, esquema, l.GetString(0), l.GetInt64(1), l.GetInt64(2), l.GetInt64(3) < 0 ? null : l.GetInt64(3)),
            ct,
            new NpgsqlParameter("esquema", esquema));
}
