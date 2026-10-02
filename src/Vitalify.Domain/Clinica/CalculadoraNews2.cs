namespace Vitalify.Domain.Clinica;

public enum NivelRiesgoNews2
{
    /// <summary>Total 0–4 sin ningún parámetro en 3.</summary>
    Bajo = 1,

    /// <summary>Total 0–4 con algún parámetro en 3 (respuesta urgente en sala).</summary>
    BajoMedio = 2,

    /// <summary>Total 5–6.</summary>
    Medio = 3,

    /// <summary>Total 7 o más.</summary>
    Alto = 4,
}

/// <summary>
/// Resultado de NEWS2. Si <see cref="Faltantes"/> no está vacío, el total es <b>parcial</b>: el riesgo real puede
/// ser mayor, porque los parámetros ausentes suman 0.
/// </summary>
public sealed record ResultadoNews2(
    int Total, IReadOnlyDictionary<ParametroClinico, int> Puntos, IReadOnlyList<ParametroClinico> Faltantes, NivelRiesgoNews2 Nivel)
{
    public bool Completo => Faltantes.Count == 0;

    public bool AlgunParametroEnTres => Puntos.Values.Any(p => p == 3);
}

/// <summary>
/// National Early Warning Score 2 (Royal College of Physicians, 2017). Función pura: puntúa cada parámetro
/// disponible con las tablas oficiales y clasifica el riesgo.
/// </summary>
public static class CalculadoraNews2
{
    public static ResultadoNews2 Calcular(ParametrosClinicos p, EscalaSpo2 escala = EscalaSpo2.Escala1)
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

        Puntuar(ParametroClinico.Fr, p.Fr, Fr);
        Puntuar(ParametroClinico.Spo2, p.Spo2, s => Spo2(s, escala, p.OxigenoSuplementario == true));
        Puntuar(ParametroClinico.OxigenoSuplementario, p.OxigenoSuplementario, o => o ? 2 : 0);
        Puntuar(ParametroClinico.Pas, p.Pas, Pas);
        Puntuar(ParametroClinico.Fc, p.Fc, Fc);
        Puntuar(ParametroClinico.Conciencia, p.Conciencia, c => c == NivelConciencia.Alerta ? 0 : 3);
        Puntuar(ParametroClinico.Temperatura, p.Temperatura, Temperatura);

        var total = puntos.Values.Sum();
        var nivel = total >= 7 ? NivelRiesgoNews2.Alto
            : total >= 5 ? NivelRiesgoNews2.Medio
            : puntos.Values.Any(v => v == 3) ? NivelRiesgoNews2.BajoMedio
            : NivelRiesgoNews2.Bajo;

        return new ResultadoNews2(total, puntos, faltantes, nivel);
    }

    /// <summary>≤8 → 3; 9–11 → 1; 12–20 → 0; 21–24 → 2; ≥25 → 3.</summary>
    public static int Fr(int rpm) => rpm switch
    {
        <= 8 => 3,
        <= 11 => 1,
        <= 20 => 0,
        <= 24 => 2,
        _ => 3,
    };

    /// <summary>
    /// Escala 1: ≤91 → 3; 92–93 → 2; 94–95 → 1; ≥96 → 0.
    /// Escala 2: ≤83 → 3; 84–85 → 2; 86–87 → 1; 88–92 (o ≥93 con aire) → 0; con oxígeno 93–94 → 1, 95–96 → 2, ≥97 → 3.
    /// </summary>
    public static int Spo2(int porcentaje, EscalaSpo2 escala, bool conOxigeno)
    {
        if (escala == EscalaSpo2.Escala1)
        {
            return porcentaje switch
            {
                <= 91 => 3,
                <= 93 => 2,
                <= 95 => 1,
                _ => 0,
            };
        }

        return porcentaje switch
        {
            <= 83 => 3,
            <= 85 => 2,
            <= 87 => 1,
            <= 92 => 0,
            _ when !conOxigeno => 0,
            <= 94 => 1,
            <= 96 => 2,
            _ => 3,
        };
    }

    /// <summary>≤90 → 3; 91–100 → 2; 101–110 → 1; 111–219 → 0; ≥220 → 3.</summary>
    public static int Pas(int mmHg) => mmHg switch
    {
        <= 90 => 3,
        <= 100 => 2,
        <= 110 => 1,
        <= 219 => 0,
        _ => 3,
    };

    /// <summary>≤40 → 3; 41–50 → 1; 51–90 → 0; 91–110 → 1; 111–130 → 2; ≥131 → 3.</summary>
    public static int Fc(int lpm) => lpm switch
    {
        <= 40 => 3,
        <= 50 => 1,
        <= 90 => 0,
        <= 110 => 1,
        <= 130 => 2,
        _ => 3,
    };

    /// <summary>≤35,0 → 3; 35,1–36,0 → 1; 36,1–38,0 → 0; 38,1–39,0 → 1; ≥39,1 → 2.</summary>
    public static int Temperatura(decimal celsius) => celsius switch
    {
        <= 35.0m => 3,
        <= 36.0m => 1,
        <= 38.0m => 0,
        <= 39.0m => 1,
        _ => 2,
    };
}
