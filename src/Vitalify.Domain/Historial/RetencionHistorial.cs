using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Historial;

/// <summary>
/// Cuánto tiempo se guardan crudas las lecturas del wearable. Un wearable manda 8640 lecturas al día y el plan gratuito
/// de Supabase tiene 500 MB, así que las de un episodio ya cerrado se pasan a un resumen de 5 minutos (promedio,
/// mínimo y máximo por variable) y se borran las crudas. Es la única excepción a "no borrar": solo se borran lecturas
/// crudas que ya quedaron resumidas, en la misma sentencia.
/// <para>
/// Se resume el episodio completo cuando su egreso cumple <see cref="RetencionCrudo"/>: así una hospitalización activa y
/// las lecturas de menos de ese tiempo nunca se tocan, y un episodio no queda mitad crudo y mitad resumido.
/// </para>
/// </summary>
public sealed record PoliticaRetencion
{
    /// <summary>Tamaño de cada grupo del resumen. Los grupos se alinean a la época UNIX, como los de <c>agrupar=5m</c>.</summary>
    public static readonly TimeSpan IntervaloResumen = TimeSpan.FromMinutes(5);

    public static readonly PoliticaRetencion PorDefecto = new(TimeSpan.FromDays(30));

    public PoliticaRetencion(TimeSpan retencionCrudo)
    {
        if (retencionCrudo < TimeSpan.FromDays(1))
        {
            throw new ExcepcionDeDominio("Las lecturas crudas se guardan al menos un día.");
        }

        RetencionCrudo = retencionCrudo;
    }

    public TimeSpan RetencionCrudo { get; }

    /// <summary>Los episodios egresados antes de este instante ya se pueden resumir.</summary>
    public DateTime Corte(DateTime ahora) => ahora - RetencionCrudo;

    /// <summary>Solo un episodio finalizado cuyo egreso es anterior al corte.</summary>
    public bool DebeResumirse(DateTime? egresoEn, DateTime ahora) => egresoEn is { } egreso && egreso < Corte(ahora);
}

/// <summary>Una tarea que corre una vez al día a una hora local (por ejemplo, la retención a las 03:00 de Lima).</summary>
public static class ProgramaDiario
{
    /// <summary>La próxima vez, estrictamente posterior a <paramref name="ahoraUtc"/>, que en la zona son las <paramref name="hora"/>.</summary>
    public static DateTime Proxima(DateTime ahoraUtc, TimeOnly hora, TimeZoneInfo zona)
    {
        ArgumentNullException.ThrowIfNull(zona);
        var ahoraLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(ahoraUtc, DateTimeKind.Utc), zona);
        var hoy = DateOnly.FromDateTime(ahoraLocal);
        for (var dia = hoy; ; dia = dia.AddDays(1))
        {
            var local = DateTime.SpecifyKind(dia.ToDateTime(hora), DateTimeKind.Unspecified);
            if (zona.IsInvalidTime(local))
            {
                local = local.AddHours(1); // un salto de horario de verano: la hora no existe ese día
            }

            var utc = TimeZoneInfo.ConvertTimeToUtc(local, zona);
            if (utc > ahoraUtc)
            {
                return utc;
            }
        }
    }
}
