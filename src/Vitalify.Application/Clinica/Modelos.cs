using Vitalify.Domain.Clinica;

namespace Vitalify.Application.Clinica;

/// <summary>Parámetros del motor clínico (sección <c>Clinico</c>).</summary>
/// <param name="ToleranciaToma">
/// Margen después de la próxima toma manual durante el que sus valores siguen contando (30 min por defecto). Una toma
/// vale hasta su próxima toma más este margen, con un tope de 12 h (<see cref="FrecuenciaTomaManual.TopeVigencia"/>).
/// </param>
/// <param name="AjusteTemperaturaSensor">
/// °C que se suman a la temperatura del wearable antes de puntuar. El DS18B20 mide la piel, que está por debajo de
/// la temperatura central: este desfase se calibra midiendo con un termómetro clínico. Por defecto 0.
/// </param>
public sealed record OpcionesClinicas(TimeSpan ToleranciaToma, decimal AjusteTemperaturaSensor)
{
    public static readonly OpcionesClinicas PorDefecto = new(TimeSpan.FromMinutes(30), 0m);
}

public static class TextosClinicos
{
    public const string SinDatos = "sin-datos";

    public static string De(NivelRiesgoNews2 nivel) => nivel switch
    {
        NivelRiesgoNews2.Alto => "alto",
        NivelRiesgoNews2.Medio => "medio",
        NivelRiesgoNews2.BajoMedio => "bajo-medio",
        _ => "bajo",
    };

    public static string De(NivelRiesgoMews nivel) => nivel switch
    {
        NivelRiesgoMews.Alto => "alto",
        NivelRiesgoMews.Medio => "medio",
        _ => "bajo",
    };

    public static string De(ParametroClinico parametro) => parametro switch
    {
        ParametroClinico.Fr => "fr",
        ParametroClinico.Spo2 => "spo2",
        ParametroClinico.OxigenoSuplementario => "oxigenoSuplementario",
        ParametroClinico.Pas => "pas",
        ParametroClinico.Fc => "fc",
        ParametroClinico.Conciencia => "conciencia",
        _ => "temperatura",
    };

    /// <summary>Respuesta clínica sugerida por NEWS2 (RCP 2017) para el nivel de riesgo.</summary>
    public static string RespuestaSugerida(NivelRiesgoNews2 nivel) => nivel switch
    {
        NivelRiesgoNews2.Alto => "Respuesta de emergencia: evaluación inmediata por el equipo y monitoreo continuo.",
        NivelRiesgoNews2.Medio => "Respuesta urgente: evaluación por el médico y monitoreo al menos cada hora.",
        NivelRiesgoNews2.BajoMedio => "Respuesta urgente en sala: un parámetro en 3; evaluación médica y monitoreo al menos cada hora.",
        _ => "Monitoreo de rutina (cada 4 a 12 horas según el total).",
    };
}

public sealed record PuntajeDto(int Total, string Nivel, bool Completo, IReadOnlyDictionary<string, int> Puntos, IReadOnlyList<string> Faltantes)
{
    public static PuntajeDto De(ResultadoNews2 r) =>
        new(r.Total, TextosClinicos.De(r.Nivel), r.Completo, r.Puntos.ToDictionary(p => TextosClinicos.De(p.Key), p => p.Value),
            r.Faltantes.Select(TextosClinicos.De).ToList());

    public static PuntajeDto De(ResultadoMews r) =>
        new(r.Total, TextosClinicos.De(r.Nivel), r.Completo, r.Puntos.ToDictionary(p => TextosClinicos.De(p.Key), p => p.Value),
            r.Faltantes.Select(TextosClinicos.De).ToList());
}

public sealed record ParametrosUsadosDto(
    int? Fc, int? Spo2, decimal? Temperatura, int? Fr, int? Pas, string? Conciencia, bool? OxigenoSuplementario, string EscalaSpo2)
{
    public static ParametrosUsadosDto De(EvaluacionRiesgo e) =>
        new(e.Fc, e.Spo2, e.Temperatura, e.Fr, e.Pas, e.Conciencia?.ToString(), e.OxigenoSuplementario, e.EscalaSpo2.ToString());
}

/// <summary>
/// Evaluación de riesgo. Si <c>completo</c> es <c>false</c>, el total es parcial (faltan parámetros que suman 0) y el
/// riesgo real puede ser mayor.
/// </summary>
/// <param name="RespuestaSugerida">Igual que <paramref name="Recomendacion"/>; se mantiene por compatibilidad con el dashboard.</param>
/// <param name="Recomendacion">Respuesta clínica de NEWS2 (RCP 2017) para el nivel.</param>
/// <param name="ProximaTomaManualEn">
/// Cuándo repetir la toma manual: la última toma más 12 h (NEWS2 0), 4 h (1–4) o 1 h (un parámetro en 3, 5 o más).
/// Sin ninguna toma manual, la hora de la evaluación (está pendiente).
/// </param>
/// <param name="Avisos">Textos para la enfermera (presión extrema, preocupación clínica, escala de SpO2, checklist).</param>
public sealed record EvaluacionRiesgoDto(
    DateTime EvaluadaEn, string Origen, PuntajeDto News2, PuntajeDto Mews, string RespuestaSugerida, ParametrosUsadosDto Parametros,
    string Recomendacion, DateTime ProximaTomaManualEn, IReadOnlyList<string> Avisos)
{
    public static EvaluacionRiesgoDto Desde(EvaluacionRiesgo e, DateTime? ultimaTomaManual, IReadOnlyList<string> avisos)
    {
        var news2 = e.News2();
        var mews = e.Mews();
        var recomendacion = TextosClinicos.RespuestaSugerida(news2.Nivel);
        return new EvaluacionRiesgoDto(
            e.EvaluadaEn,
            e.Origen.ToString(),
            PuntajeDto.De(news2),
            PuntajeDto.De(mews),
            recomendacion,
            ParametrosUsadosDto.De(e),
            recomendacion,
            e.ProximaTomaManualEn ?? FrecuenciaTomaManual.Proxima(ultimaTomaManual, e.EvaluadaEn, news2),
            avisos);
    }
}

/// <param name="EscalaSpo2">Escala de SpO2 vigente de la hospitalización (<c>Escala1</c> o <c>Escala2</c>).</param>
/// <param name="UltimoBrazoPa">Brazo de la última toma de presión, para sugerirlo en la siguiente (null si no hay).</param>
public sealed record RiesgoPacienteDto(
    Guid PacienteId, Guid HospitalizacionId, EvaluacionRiesgoDto? UltimaEvaluacion, string EscalaSpo2, string? UltimoBrazoPa);

/// <summary>
/// Avisos de una evaluación, a partir de los valores usados y de las tomas manuales vigentes. No son alertas (las
/// alertas llegan en la fase 6): son textos para que la enfermera actúe en el momento.
/// </summary>
public static class AvisosClinicos
{
    public const string PasBaja = "PAS ≤ 90 mmHg: confirme con una nueva toma y avise al médico.";
    public const string PasMuyAlta = "PAS ≥ 220 mmHg: confirme con una nueva toma y avise al médico de inmediato.";
    public const string PreocupacionClinica = "Preocupación clínica registrada: se notificará al médico.";
    public const string RevisarEscalaSpo2 = "Solicite al médico revisar la escala de SpO2.";
    public const string ChecklistPresionIncompleto = "Checklist de presión incompleto: el valor puede no ser fiable.";
    public const string ObservacionInicialNoGuardada = "La observación inicial no se guardó; regístrela de nuevo.";

    /// <param name="recientes">Tomas manuales vigentes, la más reciente primero.</param>
    public static IReadOnlyList<string> Generar(EvaluacionRiesgo evaluacion, IReadOnlyList<ObservacionEnfermeria> recientes)
    {
        ArgumentNullException.ThrowIfNull(evaluacion);
        ArgumentNullException.ThrowIfNull(recientes);

        var avisos = new List<string>();
        if (evaluacion.Pas is <= 90)
        {
            avisos.Add(PasBaja);
        }
        else if (evaluacion.Pas is >= 220)
        {
            avisos.Add(PasMuyAlta);
        }

        // Para cada dato se mira la toma más reciente que lo registró: una toma posterior puede corregir a la anterior.
        if (recientes.FirstOrDefault(o => o.PreocupacionClinica is not null)?.PreocupacionClinica == true)
        {
            avisos.Add(PreocupacionClinica);
        }

        if (evaluacion.EscalaSpo2 == EscalaSpo2.Escala1
            && recientes.FirstOrDefault(o => o.ObjetivoSpo288a92 is not null)?.ObjetivoSpo288a92 == true)
        {
            avisos.Add(RevisarEscalaSpo2);
        }

        if (recientes.FirstOrDefault(o => o.Pas is not null) is { ChecklistPa: { Completo: false } })
        {
            avisos.Add(ChecklistPresionIncompleto);
        }

        return avisos;
    }
}
