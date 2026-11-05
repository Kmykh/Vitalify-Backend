namespace Vitalify.Application.Puertos;

/// <summary>Tamaño de una tabla de Vitalify en la base.</summary>
/// <param name="Contexto"><c>transaccional</c> o <c>historial</c>.</param>
/// <param name="BytesTotal">Con índices y TOAST (<c>pg_total_relation_size</c>).</param>
/// <param name="FilasAproximadas">Estimación de PostgreSQL (<c>reltuples</c>); null si la tabla nunca se analizó.</param>
public sealed record TamanoTabla(string Contexto, string Esquema, string Tabla, long BytesTotal, long BytesIndices, long? FilasAproximadas);

/// <summary>Mide cuánto ocupa la base (para vigilar los 500 MB del plan gratuito de Supabase).</summary>
public interface IMedidorAlmacenamiento
{
    /// <summary>Tamaño de toda la base del contexto transaccional (<c>pg_database_size</c>): es lo que cuenta Supabase.</summary>
    Task<long> MedirBaseAsync(CancellationToken ct = default);

    /// <summary>Las tablas de los esquemas <c>vitalify</c> y <c>vitalify_historial</c>, cada una con su contexto.</summary>
    Task<IReadOnlyList<TamanoTabla>> MedirTablasAsync(CancellationToken ct = default);
}
