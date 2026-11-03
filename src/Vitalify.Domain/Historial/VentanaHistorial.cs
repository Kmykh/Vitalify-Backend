namespace Vitalify.Domain.Historial;

/// <summary>
/// Tamaño de los grupos de una serie del historial (<c>agrupar=</c>). Los grupos se alinean a la hora UTC, que en Lima
/// (UTC−5, sin horario de verano) también es una hora en punto.
/// </summary>
public sealed record IntervaloAgrupacion(string Codigo, TimeSpan Duracion)
{
    public static readonly IntervaloAgrupacion UnMinuto = new("1m", TimeSpan.FromMinutes(1));
    public static readonly IntervaloAgrupacion CincoMinutos = new("5m", TimeSpan.FromMinutes(5));
    public static readonly IntervaloAgrupacion QuinceMinutos = new("15m", TimeSpan.FromMinutes(15));
    public static readonly IntervaloAgrupacion UnaHora = new("1h", TimeSpan.FromHours(1));

    /// <summary>Signos vitales: una lectura cada ~10 s, así que desde 1 minuto ya resume.</summary>
    public static readonly IReadOnlyList<IntervaloAgrupacion> ParaSignos = [UnMinuto, CincoMinutos, QuinceMinutos, UnaHora];

    /// <summary>Puntajes de riesgo: para la tendencia basta desde 5 minutos.</summary>
    public static readonly IReadOnlyList<IntervaloAgrupacion> ParaRiesgo = [CincoMinutos, QuinceMinutos, UnaHora];

    public int Segundos => (int)Duracion.TotalSeconds;

    /// <summary>El intervalo con ese código (sin distinguir mayúsculas) entre los permitidos, o null.</summary>
    public static IntervaloAgrupacion? Buscar(string? codigo, IReadOnlyList<IntervaloAgrupacion> permitidos)
    {
        ArgumentNullException.ThrowIfNull(permitidos);
        var limpio = codigo?.Trim().ToLowerInvariant();
        return permitidos.FirstOrDefault(i => i.Codigo == limpio);
    }

    /// <summary>Inicio del grupo que contiene <paramref name="instante"/>.</summary>
    public DateTime InicioDe(DateTime instante) =>
        new(instante.Ticks - (instante.Ticks % Duracion.Ticks), DateTimeKind.Utc);
}

/// <summary>Rango de una consulta del historial ya resuelto, o el error del campo que lo impide.</summary>
public sealed record VentanaResuelta(DateTime Desde, DateTime Hasta, string? CampoInvalido = null, string? Mensaje = null)
{
    public bool EsValida => CampoInvalido is null;
}

/// <summary>
/// Rango de tiempo de las consultas del historial de un episodio:
/// <list type="bullet">
/// <item>Por defecto, el episodio completo: del ingreso al egreso (o hasta ahora si sigue hospitalizado).</item>
/// <item>Sin agrupar, como máximo <see cref="MaximoSinAgrupar"/> (7 días), para no devolver cientos de miles de lecturas.</item>
/// <item>Agrupado, el episodio completo aunque dure más.</item>
/// </list>
/// </summary>
public static class VentanaHistorial
{
    public static readonly TimeSpan MaximoSinAgrupar = TimeSpan.FromDays(7);

    public static VentanaResuelta Resolver(
        DateTime ingresoEn, DateTime? egresoEn, DateTime ahora, DateTime? desde, DateTime? hasta, bool agrupado)
    {
        var finEpisodio = egresoEn ?? (ahora > ingresoEn ? ahora : ingresoEn);
        var inicio = desde ?? ingresoEn;
        var fin = hasta ?? finEpisodio;
        if (inicio > fin)
        {
            return new VentanaResuelta(inicio, fin, "hasta", "hasta debe ser posterior a desde.");
        }

        var maximo = agrupado
            ? (finEpisodio - ingresoEn > MaximoSinAgrupar ? finEpisodio - ingresoEn : MaximoSinAgrupar)
            : MaximoSinAgrupar;
        if (fin - inicio > maximo)
        {
            var mensaje = agrupado
                ? "Con agrupar, el rango no puede superar el episodio."
                : $"Sin agrupar, el rango no puede superar {MaximoSinAgrupar.TotalDays:0} días: usa agrupar o un rango más corto.";
            return new VentanaResuelta(inicio, fin, desde is null && hasta is null ? "agrupar" : "desde", mensaje);
        }

        return new VentanaResuelta(inicio, fin);
    }
}
