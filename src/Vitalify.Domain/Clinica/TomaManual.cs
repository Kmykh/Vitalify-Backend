using Vitalify.Domain.Comun;
using Vitalify.Domain.Telemetria;
using static System.FormattableString;

namespace Vitalify.Domain.Clinica;

public enum DispositivoOxigeno
{
    CanulaNasal = 1,
    MascarillaSimple = 2,
    Venturi = 3,
    MascarillaReservorio = 4,
    AltoFlujo = 5,
}

public enum PosicionPresion
{
    Sentado = 1,
    Acostado = 2,
    DePie = 3,
}

public enum BrazoPresion
{
    Izquierdo = 1,
    Derecho = 2,
}

public enum MetodoPresion
{
    Automatico = 1,
    Manual = 2,
}

/// <summary>
/// Condiciones de la toma de presión (RM 031-2015/MINSA, Anexo 4). No bloquean el registro, pero un checklist
/// incompleto genera el aviso "el valor puede no ser fiable".
/// </summary>
public sealed record ChecklistPresion(
    bool Reposo5Min, bool BrazoApoyadoAlturaCorazon, bool BrazaleteTallaCorrecta, bool SinCafeTabacoEsfuerzo30Min, bool TensiometroValidado)
{
    public bool Completo => Reposo5Min && BrazoApoyadoAlturaCorazon && BrazaleteTallaCorrecta && SinCafeTabacoEsfuerzo30Min && TensiometroValidado;
}

public sealed record LecturaPresion(int Pas, int Pad, DateTime? MedidaEn);

/// <summary>
/// Toma de presión arterial de la enfermera: de 1 a 3 lecturas. Se registra el promedio redondeado. Si hay dos
/// lecturas que difieren en más de <see cref="DiferenciaMaximaMmHg"/> mmHg (sistólica o diastólica), hace falta una
/// tercera antes de registrar.
/// </summary>
public sealed record TomaPresion
{
    public const int MaximoLecturas = 3;
    public const int DiferenciaMaximaMmHg = 10;

    private TomaPresion(
        IReadOnlyList<LecturaPresion> lecturas, ChecklistPresion? checklist, PosicionPresion? posicion, BrazoPresion? brazo, MetodoPresion? metodo)
    {
        Lecturas = lecturas;
        Checklist = checklist;
        Posicion = posicion;
        Brazo = brazo;
        Metodo = metodo;
        Pas = Promedio(lecturas.Select(l => l.Pas));
        Pad = Promedio(lecturas.Select(l => l.Pad));
    }

    public IReadOnlyList<LecturaPresion> Lecturas { get; }
    public ChecklistPresion? Checklist { get; }
    public PosicionPresion? Posicion { get; }
    public BrazoPresion? Brazo { get; }
    public MetodoPresion? Metodo { get; }

    /// <summary>Promedio redondeado (0,5 hacia arriba) de las sistólicas.</summary>
    public int Pas { get; }

    /// <summary>Promedio redondeado (0,5 hacia arriba) de las diastólicas.</summary>
    public int Pad { get; }

    /// <summary>Dos lecturas que difieren en más de 10 mmHg en la sistólica o en la diastólica.</summary>
    public static bool RequiereTerceraLectura(IReadOnlyList<LecturaPresion> lecturas) =>
        lecturas.Count == 2
        && (Math.Abs(lecturas[0].Pas - lecturas[1].Pas) > DiferenciaMaximaMmHg
            || Math.Abs(lecturas[0].Pad - lecturas[1].Pad) > DiferenciaMaximaMmHg);

    public static TomaPresion Crear(
        IReadOnlyList<LecturaPresion> lecturas, ChecklistPresion? checklist, PosicionPresion? posicion, BrazoPresion? brazo,
        MetodoPresion? metodo, RangosFisiologicos rangos)
    {
        ArgumentNullException.ThrowIfNull(lecturas);
        ArgumentNullException.ThrowIfNull(rangos);

        if (lecturas.Count is < 1 or > MaximoLecturas)
        {
            throw new ExcepcionDeDominio($"La toma de presión debe tener entre 1 y {MaximoLecturas} lecturas.");
        }

        foreach (var lectura in lecturas)
        {
            if (!rangos.EsValido(VariableSigno.Pas, lectura.Pas) || !rangos.EsValido(VariableSigno.Pad, lectura.Pad))
            {
                throw new ExcepcionDeDominio(Invariant($"Presión fuera del rango posible (PAS {rangos.Pas}, PAD {rangos.Pad})."));
            }

            if (lectura.Pas <= lectura.Pad)
            {
                throw new ExcepcionDeDominio("En cada lectura la sistólica debe ser mayor que la diastólica.");
            }
        }

        if (RequiereTerceraLectura(lecturas))
        {
            throw new ExcepcionDeDominio(
                $"Las dos lecturas difieren en más de {DiferenciaMaximaMmHg} mmHg: tome una tercera lectura.");
        }

        if ((posicion is { } p && !Enum.IsDefined(p)) || (brazo is { } b && !Enum.IsDefined(b)) || (metodo is { } m && !Enum.IsDefined(m)))
        {
            throw new ExcepcionDeDominio("La posición, el brazo o el método de la toma de presión no son válidos.");
        }

        return new TomaPresion(lecturas.ToList(), checklist, posicion, brazo, metodo);
    }

    private static int Promedio(IEnumerable<int> valores) =>
        (int)Math.Round(valores.Average(v => (decimal)v), MidpointRounding.AwayFromZero);
}

/// <summary>Oxígeno suplementario: si lo recibe, con qué dispositivo, a qué flujo y, en Venturi o alto flujo, la FiO2.</summary>
public sealed record OxigenoAdministrado
{
    public const decimal FlujoMinimoLpm = 0.5m;
    public const decimal FlujoMaximoLpm = 60m;
    public const int Fio2Minima = 21;
    public const int Fio2Maxima = 100;

    private OxigenoAdministrado(bool recibe, DispositivoOxigeno? dispositivo, decimal? flujoLpm, int? fio2, bool? objetivo88a92)
    {
        Recibe = recibe;
        Dispositivo = dispositivo;
        FlujoLpm = flujoLpm;
        Fio2 = fio2;
        Objetivo88a92 = objetivo88a92;
    }

    public bool Recibe { get; }
    public DispositivoOxigeno? Dispositivo { get; }
    public decimal? FlujoLpm { get; }
    public int? Fio2 { get; }

    /// <summary>El paciente tiene indicado un objetivo de SpO2 de 88–92 % (corresponde a la escala 2 de NEWS2).</summary>
    public bool? Objetivo88a92 { get; }

    public static bool AdmiteFio2(DispositivoOxigeno? dispositivo) => dispositivo is DispositivoOxigeno.Venturi or DispositivoOxigeno.AltoFlujo;

    public static OxigenoAdministrado Crear(bool recibe, DispositivoOxigeno? dispositivo, decimal? flujoLpm, int? fio2, bool? objetivo88a92)
    {
        if (!recibe && (dispositivo is not null || flujoLpm is not null || fio2 is not null))
        {
            throw new ExcepcionDeDominio("Si el paciente no recibe oxígeno, no se registra dispositivo, flujo ni FiO2.");
        }

        if (recibe && dispositivo is null)
        {
            throw new ExcepcionDeDominio("Indique el dispositivo de oxígeno.");
        }

        if (dispositivo is { } d && !Enum.IsDefined(d))
        {
            throw new ExcepcionDeDominio("El dispositivo de oxígeno no es válido.");
        }

        if (flujoLpm is { } f && (f < FlujoMinimoLpm || f > FlujoMaximoLpm))
        {
            throw new ExcepcionDeDominio(Invariant($"El flujo de oxígeno debe estar entre {FlujoMinimoLpm} y {FlujoMaximoLpm} L/min."));
        }

        if (fio2 is { } fi && (fi < Fio2Minima || fi > Fio2Maxima || !AdmiteFio2(dispositivo)))
        {
            throw new ExcepcionDeDominio($"La FiO2 va de {Fio2Minima} a {Fio2Maxima} % y solo se registra con Venturi o alto flujo.");
        }

        return new OxigenoAdministrado(recibe, dispositivo, flujoLpm, fio2, objetivo88a92);
    }
}

/// <summary>Contexto de la toma que no suma puntos pero ayuda a interpretarla.</summary>
/// <param name="Dolor">Escala numérica de 0 a 10.</param>
public sealed record ContextoClinico(int? Dolor, bool? AnsiedadOEsfuerzoReciente, bool? AntipireticoUltimas4h, bool? Betabloqueadores)
{
    public const int DolorMaximo = 10;

    public static ContextoClinico Crear(int? dolor, bool? ansiedadOEsfuerzoReciente, bool? antipireticoUltimas4h, bool? betabloqueadores)
    {
        if (dolor is < 0 or > DolorMaximo)
        {
            throw new ExcepcionDeDominio($"El dolor va de 0 a {DolorMaximo}.");
        }

        return new ContextoClinico(dolor, ansiedadOEsfuerzoReciente, antipireticoUltimas4h, betabloqueadores);
    }
}

/// <summary>
/// Respuestas del asistente de conciencia (ACVPU en cascada). El nivel lo calcula el dominio, no la app:
/// <list type="number">
/// <item><see cref="DespiertoCoherente"/>: ¿está despierto, abre los ojos solo y responde con coherencia?</item>
/// <item>Si sí, <see cref="ConfusionNueva"/>: no → A (alerta); sí → C (confusión nueva). Con confusión basal, la
/// pregunta es "¿más confundido que de costumbre?".</item>
/// <item>Si no, <see cref="RespondeVoz"/> → V; si no, <see cref="RespondeDolor"/> → P; si no → U.</item>
/// </list>
/// </summary>
public sealed record RespuestasConciencia(bool? DespiertoCoherente, bool? ConfusionNueva = null, bool? RespondeVoz = null, bool? RespondeDolor = null)
{
    /// <summary>La siguiente pregunta de la cascada que falta responder, o null si ya se puede calcular el nivel.</summary>
    public string? PreguntaPendiente => DespiertoCoherente switch
    {
        null => "despiertoCoherente",
        true => ConfusionNueva is null ? "confusionNueva" : null,
        false => RespondeVoz switch
        {
            null => "respondeVoz",
            true => null,
            false => RespondeDolor is null ? "respondeDolor" : null,
        },
    };

    public NivelConciencia Nivel => PreguntaPendiente is { } pendiente
        ? throw new ExcepcionDeDominio($"Falta responder {pendiente} para calcular el nivel de conciencia.")
        : DespiertoCoherente == true
            ? (ConfusionNueva == true ? NivelConciencia.ConfusionNueva : NivelConciencia.Alerta)
            : RespondeVoz == true ? NivelConciencia.RespondeVoz
            : RespondeDolor == true ? NivelConciencia.RespondeDolor
            : NivelConciencia.NoResponde;
}

/// <summary>
/// Cada cuánto debe repetirse la toma manual según NEWS2 (RCP 2017): 0 → cada 12 h; 1–4 → cada 4 h; un parámetro en
/// 3 (bajo-medio) o 5–6 → cada hora; 7 o más → cada hora como mínimo (y monitoreo continuo).
/// </summary>
public static class FrecuenciaTomaManual
{
    public static TimeSpan Intervalo(ResultadoNews2 news2)
    {
        ArgumentNullException.ThrowIfNull(news2);
        return news2.Nivel switch
        {
            NivelRiesgoNews2.Alto or NivelRiesgoNews2.Medio or NivelRiesgoNews2.BajoMedio => TimeSpan.FromHours(1),
            _ => news2.Total == 0 ? TimeSpan.FromHours(12) : TimeSpan.FromHours(4),
        };
    }

    /// <summary>
    /// Próxima toma: la última toma manual más el intervalo. Sin ninguna toma manual, la toma está pendiente desde ya
    /// (<paramref name="evaluadaEn"/>).
    /// </summary>
    public static DateTime Proxima(DateTime? ultimaTomaManual, DateTime evaluadaEn, ResultadoNews2 news2) =>
        ultimaTomaManual is { } ultima ? ultima + Intervalo(news2) : evaluadaEn;
}
