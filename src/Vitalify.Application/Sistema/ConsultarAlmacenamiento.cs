using Vitalify.Application.Puertos;

namespace Vitalify.Application.Sistema;

/// <summary>Sección <c>Sistema</c>: <c>Sistema__LimiteAlmacenamientoMb</c> (500, el plan gratuito de Supabase).</summary>
public sealed record OpcionesSistema(long LimiteBytes)
{
    public const long BytesPorMb = 1024 * 1024;

    public static OpcionesSistema PorDefecto { get; } = new(500 * BytesPorMb);
}

public sealed record TablaAlmacenamientoDto(
    string Contexto, string Esquema, string Tabla, long Bytes, decimal Megabytes, long BytesIndices, long? FilasAproximadas);

/// <param name="BytesBase">Toda la base (incluye lo que Supabase usa para sí), que es lo que cuenta para el límite.</param>
/// <param name="PorcentajeUsado">De <paramref name="BytesBase"/> sobre el límite.</param>
/// <param name="BytesVitalify">Solo las tablas de los esquemas de Vitalify.</param>
/// <param name="Tablas">De la más grande a la más pequeña.</param>
public sealed record AlmacenamientoDto(
    DateTime MedidoEn, long BytesBase, decimal MegabytesBase, long LimiteBytes, decimal PorcentajeUsado, long BytesVitalify,
    IReadOnlyList<TablaAlmacenamientoDto> Tablas);

/// <summary>
/// Cuánto ocupa la base y cada tabla (<c>pg_total_relation_size</c>), para que el administrador vigile el límite. No
/// lee ningún dato de pacientes: solo tamaños y filas aproximadas.
/// </summary>
public sealed class ConsultarAlmacenamiento(IMedidorAlmacenamiento medidor, OpcionesSistema opciones, IReloj reloj)
{
    public async Task<AlmacenamientoDto> EjecutarAsync(CancellationToken ct = default)
    {
        var bytesBase = await medidor.MedirBaseAsync(ct);
        var tablas = (await medidor.MedirTablasAsync(ct))
            .OrderByDescending(t => t.BytesTotal).ThenBy(t => t.Esquema).ThenBy(t => t.Tabla)
            .Select(t => new TablaAlmacenamientoDto(t.Contexto, t.Esquema, t.Tabla, t.BytesTotal, Mb(t.BytesTotal), t.BytesIndices, t.FilasAproximadas))
            .ToList();

        return new AlmacenamientoDto(
            reloj.AhoraUtc, bytesBase, Mb(bytesBase), opciones.LimiteBytes,
            Math.Round(100m * bytesBase / opciones.LimiteBytes, 1), tablas.Sum(t => t.Bytes), tablas);
    }

    private static decimal Mb(long bytes) => Math.Round((decimal)bytes / OpcionesSistema.BytesPorMb, 2);
}
