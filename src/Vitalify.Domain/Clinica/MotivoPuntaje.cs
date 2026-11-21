using System.Globalization;

namespace Vitalify.Domain.Clinica;

/// <summary>
/// El parámetro que más sube el NEWS2 de una evaluación, para mostrarlo en la lista de pacientes y en la alerta sin abrir la
/// ficha ("FR 26 rpm: +3"). Sale del mismo desglose que <c>GET /riesgo</c> (<see cref="EvaluacionRiesgo.News2"/>). A igual
/// puntaje gana el primero de la tabla NEWS2: FR, SpO2, oxígeno, PAS, FC, conciencia y temperatura.
/// </summary>
/// <param name="Valor">El valor que usó el motor, con su unidad ("26 rpm", "93 %", "ACVPU V").</param>
public sealed record MotivoPuntaje(ParametroClinico Parametro, int Puntos, string Valor)
{
    /// <summary>Null si ningún parámetro suma puntos.</summary>
    public static MotivoPuntaje? De(EvaluacionRiesgo evaluacion)
    {
        var mayor = evaluacion.News2().Puntos
            .Where(p => p.Value > 0)
            .OrderByDescending(p => p.Value)
            .ThenBy(p => (int)p.Key)
            .Select(p => (KeyValuePair<ParametroClinico, int>?)p)
            .FirstOrDefault();
        return mayor is { } m ? new MotivoPuntaje(m.Key, m.Value, Texto(m.Key, evaluacion.Parametros)) : null;
    }

    private static string Texto(ParametroClinico parametro, ParametrosClinicos p) => parametro switch
    {
        ParametroClinico.Fr => $"{p.Fr} rpm",
        ParametroClinico.Spo2 => $"{p.Spo2} %",
        ParametroClinico.OxigenoSuplementario => "recibe oxígeno",
        ParametroClinico.Pas => $"{p.Pas} mmHg",
        ParametroClinico.Fc => $"{p.Fc} lpm",
        ParametroClinico.Conciencia => p.Conciencia switch
        {
            NivelConciencia.ConfusionNueva => "ACVPU C",
            NivelConciencia.RespondeVoz => "ACVPU V",
            NivelConciencia.RespondeDolor => "ACVPU P",
            NivelConciencia.NoResponde => "ACVPU U",
            _ => "ACVPU A",
        },
        _ => p.Temperatura is { } t ? t.ToString("0.0", CultureInfo.InvariantCulture) + " °C" : string.Empty,
    };
}
