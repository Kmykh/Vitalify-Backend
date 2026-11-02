namespace Vitalify.Domain.Clinica;

/// <summary>Nivel de conciencia ACVPU (NEWS2). MEWS usa AVPU: la confusión nueva se puntúa como V.</summary>
public enum NivelConciencia
{
    Alerta = 1,
    ConfusionNueva = 2,
    RespondeVoz = 3,
    RespondeDolor = 4,
    NoResponde = 5,
}

/// <summary>
/// Escala de SpO2 de NEWS2. La 2 es para pacientes con insuficiencia respiratoria hipercápnica (objetivo 88–92 %)
/// y la indica el médico; por defecto se usa la 1.
/// </summary>
public enum EscalaSpo2
{
    Escala1 = 1,
    Escala2 = 2,
}

public enum ParametroClinico
{
    Fr = 1,
    Spo2 = 2,
    OxigenoSuplementario = 3,
    Pas = 4,
    Fc = 5,
    Conciencia = 6,
    Temperatura = 7,
}

/// <summary>
/// Valores con los que se calcula el riesgo. Cualquiera puede faltar: el wearable de Vitalify mide FC, SpO2 y
/// temperatura; FR, PAS, conciencia y oxígeno suplementario los registra la enfermera.
/// </summary>
public sealed record ParametrosClinicos(
    int? Fr = null,
    int? Spo2 = null,
    bool? OxigenoSuplementario = null,
    int? Pas = null,
    int? Fc = null,
    NivelConciencia? Conciencia = null,
    decimal? Temperatura = null);
