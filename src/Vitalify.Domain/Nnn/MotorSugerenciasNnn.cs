using System.Globalization;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Clinica;

namespace Vitalify.Domain.Nnn;

public enum PrioridadSugerencia
{
    Baja = 1,
    Media = 2,
    Alta = 3,
}

/// <summary>
/// Lo que el motor mira de un paciente: la última evaluación de riesgo (con los puntos NEWS2 de cada parámetro), las
/// alertas abiertas, la última toma manual <b>que todavía cuenta</b> (dolor y preocupación clínica; el ACVPU y el oxígeno
/// los trae la evaluación) y los diagnósticos que ya están activos en el plan, para no sugerirlos otra vez.
/// </summary>
/// <param name="DiagnosticosExcluidos">Además de los activos, los que la enfermera descartó en este turno.</param>
public sealed record EntradaMotorNnn(
    EvaluacionRiesgo? Evaluacion,
    IReadOnlyList<AlertaClinica> AlertasAbiertas,
    ObservacionEnfermeria? UltimaObservacion,
    IReadOnlyCollection<string> DiagnosticosActivos,
    IReadOnlyCollection<string>? DiagnosticosExcluidos = null);

/// <summary>Lo que propone una regla: un diagnóstico NANDA-I con sus NIC y NOC, el motivo legible y la prioridad.</summary>
public sealed record CandidataNnn(
    string Diagnostico, IReadOnlyList<string> Intervenciones, IReadOnlyList<string> Resultados, string Motivo,
    PrioridadSugerencia Prioridad, Guid? AlertaId = null);

/// <summary>Una regla del motor. <see cref="Evaluar"/> devuelve null si no se cumple.</summary>
public sealed record ReglaNnn(string Id, string Descripcion, Func<ContextoReglasNnn, CandidataNnn?> Evaluar);

/// <summary>
/// Una sugerencia para la enfermera. Si dos reglas proponen el mismo diagnóstico se juntan en una sola (sus NIC, NOC y
/// motivos se unen; la prioridad es la mayor). <see cref="Regla"/> es la primera que lo propuso.
/// </summary>
public sealed record SugerenciaNnn(
    string DiagnosticoCodigo, IReadOnlyList<string> Intervenciones, IReadOnlyList<string> Resultados, IReadOnlyList<string> Reglas,
    string Motivo, PrioridadSugerencia Prioridad, Guid? EvaluacionId, Guid? AlertaId)
{
    public string Regla => Reglas[0];
}

/// <summary>Los valores y los puntos NEWS2 que miran las reglas, calculados una sola vez por evaluación.</summary>
public sealed class ContextoReglasNnn
{
    private readonly IReadOnlyDictionary<ParametroClinico, int> _puntos;

    private ContextoReglasNnn(EntradaMotorNnn entrada, ResultadoNews2? news2)
    {
        Evaluacion = entrada.Evaluacion;
        _puntos = news2?.Puntos ?? new Dictionary<ParametroClinico, int>();
        News2Total = news2?.Total ?? 0;
        AlgunParametroEnTres = news2?.AlgunParametroEnTres ?? false;
        Dolor = entrada.UltimaObservacion?.Contexto?.Dolor;
        Caida = entrada.AlertasAbiertas.Where(a => a.Tipo == TipoAlerta.Caida && a.EstaAbierta).OrderByDescending(a => a.CreadaEn).FirstOrDefault();
        Riesgo = entrada.AlertasAbiertas.Where(a => a.Tipo == TipoAlerta.Riesgo && a.EstaAbierta).OrderByDescending(a => a.CreadaEn).FirstOrDefault();
    }

    public EvaluacionRiesgo? Evaluacion { get; }
    public int News2Total { get; }
    public bool AlgunParametroEnTres { get; }
    public int? Dolor { get; }
    public AlertaClinica? Caida { get; }

    /// <summary>La alerta de riesgo abierta, si hay: a ella se asocian las sugerencias que salen de los signos.</summary>
    public AlertaClinica? Riesgo { get; }

    public int? Fr => Evaluacion?.Fr;
    public int? Spo2 => Evaluacion?.Spo2;
    public int? Pas => Evaluacion?.Pas;
    public int? Fc => Evaluacion?.Fc;
    public decimal? Temperatura => Evaluacion?.Temperatura;
    public NivelConciencia? Conciencia => Evaluacion?.Conciencia;
    public bool ConOxigeno => Evaluacion?.OxigenoSuplementario == true;
    public EscalaSpo2 EscalaSpo2 => Evaluacion?.EscalaSpo2 ?? EscalaSpo2.Escala1;

    /// <summary>NEWS2 de 5 o más, o algún parámetro con 3 puntos: hay que avisar al médico (NIC 7710).</summary>
    public bool RequiereColaboracionMedica => News2Total >= 5 || AlgunParametroEnTres;

    /// <summary>Puntos NEWS2 del parámetro; null si no se midió.</summary>
    public int? Puntos(ParametroClinico parametro) => _puntos.TryGetValue(parametro, out var p) ? p : null;

    public static ContextoReglasNnn Desde(EntradaMotorNnn entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return new ContextoReglasNnn(entrada, entrada.Evaluacion?.News2());
    }
}

/// <summary>
/// Reglas de sugerencia NNN, versión <see cref="MotorSugerenciasNnn.VersionReglas"/>. Los umbrales son los puntos NEWS2
/// (RCP 2017) que ya calcula <see cref="CalculadoraNews2"/>, no números nuevos; las temperaturas, la PAS y la FC de las
/// reglas R3, R4, R6 y R10 son los límites de sus bandas NEWS2. Son criterio clínico versionado: viven en código, no en
/// la configuración. Para agregar una regla, se agrega aquí con su prueba y se sube la versión (ver <c>docs/nnn.md</c>).
/// </summary>
public static class ReglasNnn
{
    public const string NicColaboracionMedica = "7710";

    /// <summary>NEWS2 de 5 o más o un parámetro con 3 puntos: agrega NIC 7710 a todas las sugerencias.</summary>
    public const string ColaboracionMedica = "R11";

    public static readonly ReglaNnn PatronRespiratorio = new("R1", "FR con 2 o más puntos NEWS2", c =>
        c.Puntos(ParametroClinico.Fr) is >= 2 and var p
            ? new CandidataNnn("00032", ["3350", "3140"], ["0415"], $"FR {c.Fr} rpm: {p} puntos NEWS2", PorPuntos(p), c.Riesgo?.Id)
            : null);

    public static readonly ReglaNnn IntercambioGases = new("R2", "SpO2 con 2 o más puntos NEWS2, o con oxígeno suplementario", c =>
    {
        var puntos = c.Puntos(ParametroClinico.Spo2);
        if (puntos is not >= 2 && !c.ConOxigeno)
        {
            return null;
        }

        var motivos = new List<string>();
        if (puntos is >= 2)
        {
            motivos.Add($"SpO2 {c.Spo2} % (escala {(int)c.EscalaSpo2}): {puntos} puntos NEWS2");
        }

        if (c.ConOxigeno)
        {
            motivos.Add("recibe oxígeno suplementario");
        }

        return new CandidataNnn("00030", ["3320", "3350"], ["0402"], Unir(motivos), puntos is >= 2 ? PorPuntos(puntos.Value) : PrioridadSugerencia.Media, c.Riesgo?.Id);
    });

    public static readonly ReglaNnn Hipertermia = new("R3", "Temperatura de 38,1 °C o más", c =>
        c.Temperatura is { } t && t >= 38.1m
            ? new CandidataNnn("00007", ["3740", "6680"], ["0800"], $"Temperatura {Grados(t)}: {PuntosNews2(c.Puntos(ParametroClinico.Temperatura) ?? 0)}",
                t >= 39.1m ? PrioridadSugerencia.Media : PrioridadSugerencia.Baja, c.Riesgo?.Id)
            : null);

    public static readonly ReglaNnn Hipotermia = new("R4", "Temperatura de 35,0 °C o menos", c =>
        c.Temperatura is { } t && t <= 35.0m
            ? new CandidataNnn("00006", ["3800", "6680"], ["0800"], $"Temperatura {Grados(t)}: 3 puntos NEWS2", PrioridadSugerencia.Alta, c.Riesgo?.Id)
            : null);

    public static readonly ReglaNnn GastoCardiaco = new("R5", "PAS o FC con 2 o más puntos NEWS2 (00029 si puntúan las dos; si no, 00240)", c =>
    {
        var pas = c.Puntos(ParametroClinico.Pas);
        var fc = c.Puntos(ParametroClinico.Fc);
        if (pas is not >= 2 && fc is not >= 2)
        {
            return null;
        }

        var motivos = new List<string>();
        if (pas is >= 2)
        {
            motivos.Add($"PAS {c.Pas} mmHg: {pas} puntos NEWS2");
        }

        if (fc is >= 2)
        {
            motivos.Add($"FC {c.Fc} lpm: {fc} puntos NEWS2");
        }

        var ambas = pas is >= 2 && fc is >= 2;
        return new CandidataNnn(ambas ? "00029" : "00240", ["4150", "6680"], ["0400", "0802"], Unir(motivos),
            PorPuntos(Math.Max(pas ?? 0, fc ?? 0)), c.Riesgo?.Id);
    });

    public static readonly ReglaNnn Hipoperfusion = new("R6", "PAS de 90 o menos y FC de 131 o más", c =>
        c.Pas is <= 90 && c.Fc is >= 131
            ? new CandidataNnn("00028", ["4250", "4120"], ["0601"],
                $"PAS {c.Pas} mmHg con FC {c.Fc} lpm: posible hipoperfusión, avise al médico", PrioridadSugerencia.Alta, c.Riesgo?.Id)
            : null);

    public static readonly ReglaNnn Conciencia = new("R7", "ACVPU distinto de A (00128 si es C; si no, 00201)", c =>
        c.Conciencia is { } nivel && nivel != NivelConciencia.Alerta
            ? new CandidataNnn(nivel == NivelConciencia.ConfusionNueva ? "00128" : "00201", ["2620", "6490"], ["0912"],
                $"ACVPU {Letra(nivel)}: 3 puntos NEWS2", PrioridadSugerencia.Alta, c.Riesgo?.Id)
            : null);

    public static readonly ReglaNnn RiesgoCaidas = new("R8", "Caída detectada o ACVPU distinto de A", c =>
    {
        var alterada = c.Conciencia is { } n && n != NivelConciencia.Alerta;
        if (c.Caida is null && !alterada)
        {
            return null;
        }

        var motivos = new List<string>();
        if (c.Caida is not null)
        {
            motivos.Add("caída detectada por el wearable");
        }

        if (alterada)
        {
            motivos.Add($"ACVPU {Letra(c.Conciencia!.Value)}");
        }

        return new CandidataNnn("00303", ["6490"], ["1909"], Unir(motivos),
            c.Caida is not null ? PrioridadSugerencia.Alta : PrioridadSugerencia.Media, c.Caida?.Id ?? c.Riesgo?.Id);
    });

    public static readonly ReglaNnn DolorAgudo = new("R9", "Dolor de 4 o más en la toma manual", c =>
        c.Dolor is >= 4 and var d
            ? new CandidataNnn("00132", ["1410"], ["2102"], $"Dolor {d}/10 en la toma manual",
                d >= 7 ? PrioridadSugerencia.Media : PrioridadSugerencia.Baja, c.Riesgo?.Id)
            : null);

    public static readonly ReglaNnn RespuestaInflamatoria = new("R10", "Temperatura de 38,1 °C o más con FC de 91 o más y FR de 21 o más", c =>
        c.Temperatura is { } t && t >= 38.1m && c.Fc is >= 91 && c.Fr is >= 21
            ? new CandidataNnn("00004", ["6540", NicColaboracionMedica], ["0703"],
                $"Temperatura {Grados(t)}, FC {c.Fc} lpm y FR {c.Fr} rpm: posible respuesta inflamatoria: avise al médico",
                PrioridadSugerencia.Alta, c.Riesgo?.Id)
            : null);

    /// <summary>Las reglas que proponen diagnósticos, en el orden en que se evalúan. R11 la aplica el motor al final.</summary>
    public static readonly IReadOnlyList<ReglaNnn> Todas =
    [
        PatronRespiratorio, IntercambioGases, Hipertermia, Hipotermia, GastoCardiaco, Hipoperfusion, Conciencia, RiesgoCaidas,
        DolorAgudo, RespuestaInflamatoria,
    ];

    private static string PuntosNews2(int puntos) => puntos == 1 ? "1 punto NEWS2" : $"{puntos} puntos NEWS2";

    private static PrioridadSugerencia PorPuntos(int puntos) => puntos >= 3 ? PrioridadSugerencia.Alta : PrioridadSugerencia.Media;

    private static string Unir(IEnumerable<string> motivos)
    {
        var texto = string.Join("; ", motivos);
        return char.ToUpper(texto[0], CultureInfo.InvariantCulture) + texto[1..];
    }

    /// <summary>Con coma decimal, como se escribe en el Perú: "38,9 °C".</summary>
    internal static string Grados(decimal celsius) => celsius.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',') + " °C";

    private static string Letra(NivelConciencia nivel) => nivel switch
    {
        NivelConciencia.Alerta => "A",
        NivelConciencia.ConfusionNueva => "C (confusión nueva)",
        NivelConciencia.RespondeVoz => "V",
        NivelConciencia.RespondeDolor => "P",
        _ => "U",
    };
}

/// <summary>
/// Motor de sugerencias NNN: apoyo a la decisión, no diagnóstico automático. Propone diagnósticos NANDA-I con sus NIC y
/// NOC a partir de la evaluación NEWS2, las alertas y la toma manual; la enfermera confirma o descarta. Es puro: no lee la
/// base ni la hora.
/// </summary>
public sealed class MotorSugerenciasNnn
{
    /// <summary>Versión de las reglas (como la guía de toma manual). Cada diagnóstico confirmado la guarda.</summary>
    public const string VersionReglas = "2026.11.1";

    private readonly IReadOnlyList<ReglaNnn> _reglas;

    public MotorSugerenciasNnn(IEnumerable<ReglaNnn> reglas)
    {
        ArgumentNullException.ThrowIfNull(reglas);
        _reglas = reglas.ToList();
        if (_reglas.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != _reglas.Count)
        {
            throw new ArgumentException("Hay dos reglas con el mismo id.", nameof(reglas));
        }
    }

    public static MotorSugerenciasNnn Estandar { get; } = new(ReglasNnn.Todas);

    public IReadOnlyList<ReglaNnn> Reglas => _reglas;

    public IReadOnlyList<SugerenciaNnn> Sugerir(EntradaMotorNnn entrada)
    {
        var contexto = ContextoReglasNnn.Desde(entrada);
        var excluidos = new HashSet<string>(entrada.DiagnosticosActivos, StringComparer.Ordinal);
        excluidos.UnionWith(entrada.DiagnosticosExcluidos ?? []);

        var candidatas = _reglas
            .Select((regla, orden) => (regla, orden, candidata: regla.Evaluar(contexto)))
            .Where(x => x.candidata is not null && !excluidos.Contains(x.candidata.Diagnostico))
            .ToList();

        var colaboracion = contexto.RequiereColaboracionMedica;
        return candidatas
            .GroupBy(x => x.candidata!.Diagnostico, StringComparer.Ordinal)
            .Select(grupo =>
            {
                var c = grupo.Select(x => x.candidata!).ToList();
                var intervenciones = c.SelectMany(x => x.Intervenciones).Distinct(StringComparer.Ordinal).ToList();
                var reglas = grupo.Select(x => x.regla.Id).ToList();
                var motivo = string.Join(". ", c.Select(x => x.Motivo));
                if (colaboracion)
                {
                    if (!intervenciones.Contains(ReglasNnn.NicColaboracionMedica))
                    {
                        intervenciones.Add(ReglasNnn.NicColaboracionMedica);
                    }

                    reglas.Add(ReglasNnn.ColaboracionMedica);
                    motivo += $". NEWS2 {contexto.News2Total}{(contexto.AlgunParametroEnTres ? " con un parámetro en 3" : string.Empty)}: avise al médico";
                }

                return (orden: grupo.Min(x => x.orden), sugerencia: new SugerenciaNnn(
                    grupo.Key, intervenciones, c.SelectMany(x => x.Resultados).Distinct(StringComparer.Ordinal).ToList(), reglas, motivo,
                    c.Max(x => x.Prioridad), contexto.Evaluacion?.Id, c.Select(x => x.AlertaId).FirstOrDefault(a => a is not null)));
            })
            .OrderByDescending(x => x.sugerencia.Prioridad)
            .ThenBy(x => x.orden)
            .Select(x => x.sugerencia)
            .ToList();
    }
}

/// <summary>
/// Puntuación NOC (escala de 1 a 5) que el motor propone para un indicador vital a partir de los puntos NEWS2 de ese signo:
/// 3 puntos → 1, 2 → 2, 1 → 3 y 0 → 5. El 4 no se propone (lo elige la enfermera). Es solo una propuesta: ella la confirma
/// o la cambia.
/// </summary>
public static class ValoracionNocSugerida
{
    public const int Minimo = 1;
    public const int Maximo = 5;

    public static bool EsValida(int puntuacion) => puntuacion is >= Minimo and <= Maximo;

    public static int? DesdePuntosNews2(int? puntos) => puntos switch
    {
        3 => 1,
        2 => 2,
        1 => 3,
        0 => 5,
        _ => null,
    };

    /// <summary>Propuesta para un indicador con <paramref name="parametro"/>, con la evaluación vigente; null si no se midió.</summary>
    public static int? Para(ParametroVital parametro, EvaluacionRiesgo? evaluacion)
    {
        if (evaluacion is null)
        {
            return null;
        }

        var clinico = parametro switch
        {
            ParametroVital.Fr => ParametroClinico.Fr,
            ParametroVital.Spo2 => ParametroClinico.Spo2,
            ParametroVital.Temperatura => ParametroClinico.Temperatura,
            ParametroVital.Pas => ParametroClinico.Pas,
            ParametroVital.Fc => ParametroClinico.Fc,
            _ => ParametroClinico.Conciencia,
        };
        return evaluacion.News2().Puntos.TryGetValue(clinico, out var puntos) ? DesdePuntosNews2(puntos) : null;
    }
}
