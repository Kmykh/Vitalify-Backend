namespace Vitalify.Domain.Clinica;

/// <summary>De dónde salió el valor de un parámetro de la evaluación: el wearable o la toma manual de la enfermera.</summary>
public enum FuenteSigno
{
    Sensor = 1,
    Manual = 2,
}

/// <summary>De qué fuente salieron la FR, la PAS y la temperatura (las que pueden venir de las dos). La FC y la SpO2 siempre son del sensor; la conciencia y el oxígeno, de la toma manual.</summary>
public sealed record FuentesSignos(FuenteSigno? Fr = null, FuenteSigno? Pas = null, FuenteSigno? Temperatura = null);

/// <summary>
/// Precisión del wearable antes de puntuar (versión <see cref="Version"/>). NEWS2 se validó con tomas puntuales
/// (Weenk 2019; Downey 2018): con una lectura cada 10 s, un valor suelto por movimiento o mal contacto puntuaría y
/// dispararía alertas y sugerencias. Por eso:
/// <list type="bullet">
/// <item>la FC, la SpO2 y la temperatura del sensor se puntúan con la <b>mediana</b> de los últimos 60 s, y la FR (que ya
/// trae una ventana de 32 s) con la mediana de sus 3 últimos valores (Görges 2009; Welch 2011: retardos de 14 a 19 s
/// reducen 50–67 % las alarmas no accionables);</item>
/// <item>la FR del sensor vale 5 min (solo se mide con el paciente quieto: con 90 s, el puntaje alternaba entre completo y
/// parcial cada vez que se movía);</item>
/// <item>la temperatura de <b>piel</b> de 36,0 °C o menos no puntúa: un desfase fijo no la convierte en temperatura
/// central (el gradiente cambia con el ambiente y la perfusión). Queda como faltante: se toma con termómetro.</item>
/// </list>
/// Los valores de las ventanas son criterio de experto (CE) a validar con la asesora; el principio tiene evidencia.
/// </summary>
public static class PrecisionSensor
{
    public const string Version = "2026.12.1";

    public static readonly TimeSpan VentanaMediana = TimeSpan.FromSeconds(60);

    public const int LecturasFr = 3;

    public static readonly TimeSpan VigenciaFr = TimeSpan.FromMinutes(5);

    /// <summary>La temperatura de piel (ya corregida) de este valor o menos no puntúa.</summary>
    public const decimal TemperaturaPielMinima = 36.0m;

    public static bool TemperaturaPielPuntua(decimal? temperatura) => temperatura is > TemperaturaPielMinima;

    /// <summary>Mediana de los valores presentes (con dos centrales, su promedio redondeado); null si no hay ninguno.</summary>
    public static int? Mediana(IEnumerable<int?> valores)
    {
        var lista = valores.OfType<int>().Order().ToList();
        if (lista.Count == 0)
        {
            return null;
        }

        var medio = lista.Count / 2;
        return lista.Count % 2 == 1
            ? lista[medio]
            : (int)Math.Round((lista[medio - 1] + lista[medio]) / 2m, MidpointRounding.AwayFromZero);
    }

    public static decimal? Mediana(IEnumerable<decimal?> valores)
    {
        var lista = valores.OfType<decimal>().Order().ToList();
        if (lista.Count == 0)
        {
            return null;
        }

        var medio = lista.Count / 2;
        return lista.Count % 2 == 1
            ? lista[medio]
            : Math.Round((lista[medio - 1] + lista[medio]) / 2m, 1, MidpointRounding.AwayFromZero);
    }
}
