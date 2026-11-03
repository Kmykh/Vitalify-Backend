using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Vitalify.Application.Puertos;

namespace Vitalify.Infrastructure.Persistence.Historial.Repositorios;

/// <summary>
/// Agregaciones por intervalo de tiempo en SQL. EF Core no traduce <c>date_bin</c>, así que el grupo se calcula con
/// <c>floor(epoch / segundos)</c>: es PostgreSQL estándar y funciona igual en TimescaleDB (donde podría cambiarse por
/// <c>time_bucket</c>). Las tablas y columnas salen del modelo de EF, no se escriben a mano; los valores van siempre
/// como parámetros.
/// </summary>
internal static class ConsultasAgregadas
{
    public static string Tabla<T>(DbContext db)
    {
        var entidad = db.Model.FindEntityType(typeof(T))!;
        return $"\"{entidad.GetSchema()}\".\"{entidad.GetTableName()}\"";
    }

    public static string Columna<T>(DbContext db, string propiedad)
    {
        var entidad = db.Model.FindEntityType(typeof(T))!;
        var tabla = StoreObjectIdentifier.Table(entidad.GetTableName()!, entidad.GetSchema());
        return $"\"{entidad.FindProperty(propiedad)!.GetColumnName(tabla)}\"";
    }

    /// <summary>Expresión del inicio del grupo de <paramref name="columnaTiempo"/> para el parámetro <c>@segundos</c>.</summary>
    public static string InicioDeGrupo(string columnaTiempo) =>
        $"to_timestamp((floor(extract(epoch FROM {columnaTiempo}) / @segundos) * @segundos)::double precision)";

    public static async Task<List<T>> LeerAsync<T>(
        DbContext db, string sql, Func<DbDataReader, T> mapear, CancellationToken ct, params NpgsqlParameter[] parametros)
    {
        var conexion = db.Database.GetDbConnection();
        var abrir = conexion.State != ConnectionState.Open;
        if (abrir)
        {
            await db.Database.OpenConnectionAsync(ct);
        }

        try
        {
            await using var comando = conexion.CreateCommand();
            comando.CommandText = sql;
            comando.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            comando.Parameters.AddRange(parametros);
            await using var lector = await comando.ExecuteReaderAsync(ct);
            var filas = new List<T>();
            while (await lector.ReadAsync(ct))
            {
                filas.Add(mapear(lector));
            }

            return filas;
        }
        finally
        {
            if (abrir)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    public static decimal? Decimal(DbDataReader lector, int columna) => lector.IsDBNull(columna) ? null : lector.GetDecimal(columna);

    /// <summary>Promedio, mínimo y máximo leídos de tres columnas seguidas (null si no hubo mediciones).</summary>
    public static EstadisticaSerie? Estadistica(DbDataReader lector, int primera) =>
        lector.IsDBNull(primera)
            ? null
            : new EstadisticaSerie(lector.GetDecimal(primera), lector.GetDecimal(primera + 1), lector.GetDecimal(primera + 2));

    /// <summary>Mínimo, máximo y cantidad leídos de tres columnas seguidas (null si no hubo mediciones).</summary>
    public static RangoValor? Rango(DbDataReader lector, int primera) =>
        lector.GetInt32(primera + 2) == 0
            ? null
            : new RangoValor(lector.GetDecimal(primera), lector.GetDecimal(primera + 1), lector.GetInt32(primera + 2));

    /// <summary><c>CASE</c> que convierte el nombre guardado del enum en su número (los niveles crecen con la gravedad).</summary>
    public static string RangoDeEnum<TEnum>(string columna) where TEnum : struct, Enum =>
        "CASE " + columna + " " + string.Join(" ", Enum.GetValues<TEnum>().Select(v => $"WHEN '{v}' THEN {Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)}")) + " END";
}
