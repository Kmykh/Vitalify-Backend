using Vitalify.Domain.Clinica;

namespace Vitalify.Application.Clinica;

/// <summary>Parámetros del motor clínico (sección <c>Clinico</c>).</summary>
/// <param name="VigenciaObservaciones">Cuánto tiempo vale una observación manual para el puntaje (4 h por defecto).</param>
/// <param name="AjusteTemperaturaSensor">
/// °C que se suman a la temperatura del wearable antes de puntuar. El DS18B20 mide la piel, que está por debajo de
/// la temperatura central: este desfase se calibra midiendo con un termómetro clínico. Por defecto 0.
/// </param>
public sealed record OpcionesClinicas(TimeSpan VigenciaObservaciones, decimal AjusteTemperaturaSensor)
{
    public static readonly OpcionesClinicas PorDefecto = new(TimeSpan.FromHours(4), 0m);
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

public sealed record PuntajeDto(int Total, string Nivel, bool Completo, IReadOnlyDictionary<string, int> Puntos, IReadOnlyList<string> Faltantes);

public sealed record ParametrosUsadosDto(
    int? Fc, int? Spo2, decimal? Temperatura, int? Fr, int? Pas, string? Conciencia, bool? OxigenoSuplementario, string EscalaSpo2);

/// <summary>
/// Evaluación de riesgo. Si <c>completo</c> es <c>false</c>, el total es parcial (faltan parámetros que suman 0) y el
/// riesgo real puede ser mayor.
/// </summary>
public sealed record EvaluacionRiesgoDto(
    DateTime EvaluadaEn, string Origen, PuntajeDto News2, PuntajeDto Mews, string RespuestaSugerida, ParametrosUsadosDto Parametros)
{
    public static EvaluacionRiesgoDto Desde(EvaluacionRiesgo e)
    {
        var news2 = e.News2();
        var mews = e.Mews();
        return new EvaluacionRiesgoDto(
            e.EvaluadaEn,
            e.Origen.ToString(),
            new PuntajeDto(news2.Total, TextosClinicos.De(news2.Nivel), news2.Completo,
                news2.Puntos.ToDictionary(p => TextosClinicos.De(p.Key), p => p.Value), news2.Faltantes.Select(TextosClinicos.De).ToList()),
            new PuntajeDto(mews.Total, TextosClinicos.De(mews.Nivel), mews.Completo,
                mews.Puntos.ToDictionary(p => TextosClinicos.De(p.Key), p => p.Value), mews.Faltantes.Select(TextosClinicos.De).ToList()),
            TextosClinicos.RespuestaSugerida(news2.Nivel),
            new ParametrosUsadosDto(e.Fc, e.Spo2, e.Temperatura, e.Fr, e.Pas, e.Conciencia?.ToString(), e.OxigenoSuplementario, e.EscalaSpo2.ToString()));
    }
}

public sealed record RiesgoPacienteDto(Guid PacienteId, Guid HospitalizacionId, EvaluacionRiesgoDto? UltimaEvaluacion);
