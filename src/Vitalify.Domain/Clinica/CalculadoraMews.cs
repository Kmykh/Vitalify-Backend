namespace Vitalify.Domain.Clinica;

public enum NivelRiesgoMews
{
    /// <summary>Total 0–2.</summary>
    Bajo = 1,

    /// <summary>Total 3–4.</summary>
    Medio = 2,

    /// <summary>Total 5 o más (Subbe et al.: mayor riesgo de muerte o ingreso a UCI).</summary>
    Alto = 3,
}

/// <summary>Resultado de MEWS. Con parámetros faltantes el total es parcial (los ausentes suman 0).</summary>
public sealed record ResultadoMews(
    int Total, IReadOnlyDictionary<ParametroClinico, int> Puntos, IReadOnlyList<ParametroClinico> Faltantes, NivelRiesgoMews Nivel)
{
    public bool Completo => Faltantes.Count == 0;
}

/// <summary>
/// Modified Early Warning Score (Subbe et al., QJM 2001): PAS, FC, FR, temperatura y AVPU. No usa SpO2 ni oxígeno.
/// La confusión nueva (C de ACVPU) no existe en AVPU y se puntúa como V (1).
/// </summary>
public static class CalculadoraMews
{
    public static readonly IReadOnlyList<ParametroClinico> ParametrosUsados =
        [ParametroClinico.Pas, ParametroClinico.Fc, ParametroClinico.Fr, ParametroClinico.Temperatura, ParametroClinico.Conciencia];

    public static ResultadoMews Calcular(ParametrosClinicos p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var puntos = new Dictionary<ParametroClinico, int>();
        var faltantes = new List<ParametroClinico>();

        void Puntuar<T>(ParametroClinico parametro, T? valor, Func<T, int> tabla)
            where T : struct
        {
            if (valor is { } v)
            {
                puntos[parametro] = tabla(v);
            }
            else
            {
                faltantes.Add(parametro);
            }
        }

        Puntuar(ParametroClinico.Pas, p.Pas, Pas);
        Puntuar(ParametroClinico.Fc, p.Fc, Fc);
        Puntuar(ParametroClinico.Fr, p.Fr, Fr);
        Puntuar(ParametroClinico.Temperatura, p.Temperatura, Temperatura);
        Puntuar(ParametroClinico.Conciencia, p.Conciencia, Conciencia);

        var total = puntos.Values.Sum();
        var nivel = total >= 5 ? NivelRiesgoMews.Alto : total >= 3 ? NivelRiesgoMews.Medio : NivelRiesgoMews.Bajo;
        return new ResultadoMews(total, puntos, faltantes, nivel);
    }

    /// <summary>≤70 → 3; 71–80 → 2; 81–100 → 1; 101–199 → 0; ≥200 → 2.</summary>
    public static int Pas(int mmHg) => mmHg switch
    {
        <= 70 => 3,
        <= 80 => 2,
        <= 100 => 1,
        <= 199 => 0,
        _ => 2,
    };

    /// <summary>≤40 → 2; 41–50 → 1; 51–100 → 0; 101–110 → 1; 111–129 → 2; ≥130 → 3.</summary>
    public static int Fc(int lpm) => lpm switch
    {
        <= 40 => 2,
        <= 50 => 1,
        <= 100 => 0,
        <= 110 => 1,
        <= 129 => 2,
        _ => 3,
    };

    /// <summary>&lt;9 → 2; 9–14 → 0; 15–20 → 1; 21–29 → 2; ≥30 → 3.</summary>
    public static int Fr(int rpm) => rpm switch
    {
        < 9 => 2,
        <= 14 => 0,
        <= 20 => 1,
        <= 29 => 2,
        _ => 3,
    };

    /// <summary>&lt;35 → 2; 35–38,4 → 0; ≥38,5 → 2.</summary>
    public static int Temperatura(decimal celsius) => celsius switch
    {
        < 35.0m => 2,
        < 38.5m => 0,
        _ => 2,
    };

    /// <summary>A → 0; V (y confusión nueva) → 1; P → 2; U → 3.</summary>
    public static int Conciencia(NivelConciencia nivel) => nivel switch
    {
        NivelConciencia.Alerta => 0,
        NivelConciencia.ConfusionNueva or NivelConciencia.RespondeVoz => 1,
        NivelConciencia.RespondeDolor => 2,
        _ => 3,
    };
}
