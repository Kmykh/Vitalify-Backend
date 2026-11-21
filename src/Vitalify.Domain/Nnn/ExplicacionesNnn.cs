using Vitalify.Domain.Clinica;

namespace Vitalify.Domain.Nnn;

/// <summary>
/// El "por qué" de cada regla en lenguaje de piso, para la enfermera que recién aprende NEWS2 y la taxonomía NNN: el signo
/// con su valor y el rango en que no suma puntos (RCP 2017), sin códigos ni "puntos NEWS2". Usa los mismos valores del
/// contexto que la regla y replica su condición, así que nunca dice algo distinto del motivo técnico (que se conserva).
/// </summary>
public static class ExplicacionesNnn
{
    /// <summary>La explicación de una regla que se cumple, o null si la regla no tiene una (o no aplica al contexto).</summary>
    public static string? Para(string regla, ContextoReglasNnn c) => regla switch
    {
        "R1" => $"Respira {c.Fr} veces por minuto; lo normal es de 12 a 20.",
        "R2" => Oxigenacion(c),
        "R3" when c.Temperatura is { } t => $"Tiene fiebre: {ReglasNnn.Grados(t)} (lo normal es de 36,1 a 38,0 °C).",
        "R4" when c.Temperatura is { } t => $"Su temperatura está muy baja: {ReglasNnn.Grados(t)} (lo normal es de 36,1 a 38,0 °C).",
        "R5" => Circulacion(c),
        "R6" => $"Tiene la presión baja ({c.Pas} mmHg) y el corazón muy rápido ({c.Fc} latidos por minuto): puede que la sangre no "
            + "llegue bien a los órganos. Avisa al médico.",
        "R7" => Conciencia(c.Conciencia),
        "R8" => Caidas(c),
        "R9" => $"Refiere dolor de {c.Dolor} sobre 10.",
        "R10" when c.Temperatura is { } t => $"Tiene fiebre ({ReglasNnn.Grados(t)}) con el corazón rápido ({c.Fc} por minuto) y la "
            + $"respiración rápida ({c.Fr} por minuto): puede ser una infección o una inflamación. Avisa al médico.",
        _ => null,
    };

    /// <summary>R11: el puntaje pide que el médico evalúe al paciente.</summary>
    public static string ColaboracionMedica(ContextoReglasNnn c)
    {
        if (c.News2Total >= 5 || c.AlgunParametroEnTres)
        {
            return c.AlgunParametroEnTres
                ? $"Su NEWS2 es {c.News2Total} y un signo suma 3 puntos: avisa al médico."
                : $"Su NEWS2 es {c.News2Total} (5 o más): avisa al médico.";
        }

        return $"Su MEWS es {c.MewsTotal} (5 o más): avisa al médico.";
    }

    private static string? Oxigenacion(ContextoReglasNnn c)
    {
        var puntos = c.Puntos(ParametroClinico.Spo2);
        if (c.EscalaSpo2 == EscalaSpo2.Escala2 && c.ConOxigeno && c.Spo2 is >= 93)
        {
            return $"Con oxígeno, su saturación es {c.Spo2} %, por encima de la meta de 88 a 92 % que tiene indicada: revisa el "
                + "flujo y avisa al médico.";
        }

        var partes = new List<string>();
        if (puntos is >= 2)
        {
            partes.Add(c.EscalaSpo2 == EscalaSpo2.Escala2
                ? $"Su saturación de oxígeno es {c.Spo2} % (su meta es de 88 a 92 %)."
                : $"Su saturación de oxígeno es {c.Spo2} % (lo esperado es 96 % o más).");
        }

        if (c.ConOxigeno)
        {
            partes.Add("Recibe oxígeno suplementario.");
        }

        return partes.Count == 0 ? null : string.Join(" ", partes);
    }

    private static string? Circulacion(ContextoReglasNnn c)
    {
        var partes = new List<string>();
        if (c.Puntos(ParametroClinico.Pas) is >= 2)
        {
            partes.Add($"Su presión sistólica es {c.Pas} mmHg (lo normal es de 111 a 219).");
        }

        if (c.Puntos(ParametroClinico.Fc) is >= 2)
        {
            partes.Add($"Su corazón late {c.Fc} veces por minuto (lo normal es de 51 a 90).");
        }

        return partes.Count == 0 ? null : string.Join(" ", partes);
    }

    private static string? Conciencia(NivelConciencia? nivel) => nivel switch
    {
        NivelConciencia.ConfusionNueva => "Tiene confusión nueva: no estaba así antes.",
        NivelConciencia.RespondeVoz => "Solo responde cuando le hablas.",
        NivelConciencia.RespondeDolor => "Solo responde al dolor.",
        NivelConciencia.NoResponde => "No responde.",
        _ => null,
    };

    private static string? Caidas(ContextoReglasNnn c)
    {
        var partes = new List<string>();
        if (c.Caida is not null)
        {
            partes.Add("El wearable detectó una caída.");
        }

        if (c.Conciencia is { } n && n != NivelConciencia.Alerta)
        {
            partes.Add("Su conciencia está alterada, y eso aumenta el riesgo de caerse.");
        }

        return partes.Count == 0 ? null : string.Join(" ", partes);
    }
}
