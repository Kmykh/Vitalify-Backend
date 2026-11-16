using Vitalify.Domain.Clinica;
using Vitalify.Domain.Turnos;

namespace Vitalify.Domain.Nnn;

/// <summary>
/// Nivel de respuesta clínica de NEWS2 (RCP 2017), que fija la frecuencia de monitorización: 0; 1–4; un parámetro en 3
/// (con total 0–4); 5–6; 7 o más.
/// </summary>
public enum NivelRespuestaNews2
{
    Cero = 0,
    Bajo = 1,
    UnParametroEnTres = 2,
    Medio = 3,
    Alto = 4,
}

/// <summary>Cada cuánto toca una intervención en un nivel: por turno, según necesidad o cada cierto intervalo.</summary>
public readonly record struct Pauta(TimeSpan? Intervalo, bool PorTurno, bool SegunNecesidad)
{
    public static Pauta Cada(TimeSpan intervalo) => new(intervalo, false, false);

    public static readonly Pauta Turno = new(null, true, false);

    public static readonly Pauta Necesidad = new(null, false, true);
}

/// <summary>
/// Protocolo con el que se arma el "paquete listo" de cada sugerencia y la agenda de intervenciones (versión
/// <see cref="Version"/>). La frecuencia de la NIC 6680 sale de RCP 2017 y NICE CG50 (0: 12 h; 1–4: 4 h; un parámetro en
/// 3 o 5–6: 1 h; 7 o más: continuo); el resto, las metas y el mapa del dolor son criterio de experto (CE) a validar con
/// la asesora. Como las reglas del motor, vive en código y está versionado: cada diagnóstico confirmado guarda la versión.
/// </summary>
public static class ProtocoloNnn
{
    public const string Version = "2026.12.1";

    /// <summary>NIC 6680, monitorización de signos vitales: la registra sola una toma manual completa.</summary>
    public const string NicSignosVitales = "6680";

    /// <summary>NOC 2102, nivel del dolor: su basal sale del dolor de la toma manual.</summary>
    public const string NocDolor = "2102";

    /// <summary>NOC 1909, conducta de prevención de caídas: nunca se propone resolver su diagnóstico.</summary>
    public const string NandaRiesgoCaidas = "00303";

    /// <summary>Basal de un NOC sin parámetro vital ni dolor: la enfermera la revisa (el paquete la marca así).</summary>
    public const int BasalPorDefecto = 3;

    private static readonly Pauta Turno = Pauta.Turno;
    private static readonly Pauta Sn = Pauta.Necesidad;

    // Por nivel: 0, 1–4, un parámetro en 3, 5–6 y 7 o más.
    private static readonly Dictionary<string, Pauta[]> Tabla = new(StringComparer.Ordinal)
    {
        ["6680"] = [Horas(12), Horas(4), Horas(1), Horas(1), Minutos(30)],
        ["3350"] = [Turno, Horas(4), Horas(1), Horas(1), Minutos(30)],
        ["2620"] = [Turno, Horas(4), Horas(1), Horas(1), Minutos(30)],
        ["4150"] = [Turno, Horas(4), Horas(1), Horas(1), Minutos(30)],
        ["3320"] = [Turno, Horas(4), Horas(1), Horas(1), Minutos(30)],
        ["3740"] = [Turno, Horas(4), Horas(1), Horas(1), Minutos(30)],
        ["3800"] = [Turno, Horas(4), Horas(1), Horas(1), Minutos(30)],
        ["4250"] = [Turno, Turno, Horas(1), Horas(1), Minutos(30)],
        ["4120"] = [Turno, Turno, Horas(1), Horas(1), Minutos(30)],
        ["1410"] = [Turno, Horas(4), Horas(4), Horas(4), Horas(4)],
        ["3140"] = [Sn, Sn, Sn, Horas(1), Minutos(30)],
    };

    public static NivelRespuestaNews2 Nivel(ResultadoNews2 news2)
    {
        ArgumentNullException.ThrowIfNull(news2);
        return news2.Total >= 7 ? NivelRespuestaNews2.Alto
            : news2.Total >= 5 ? NivelRespuestaNews2.Medio
            : news2.AlgunParametroEnTres ? NivelRespuestaNews2.UnParametroEnTres
            : news2.Total == 0 ? NivelRespuestaNews2.Cero
            : NivelRespuestaNews2.Bajo;
    }

    /// <summary>
    /// El nivel con el que se agenda: sube al instante y baja solo con una toma manual completa en el nivel inferior. Por
    /// eso es el mayor entre las evaluaciones desde la última toma completa (o de la ventana que pase quien llama).
    /// </summary>
    public static NivelRespuestaNews2 NivelEfectivo(IEnumerable<EvaluacionRiesgo> desdeUltimaTomaCompleta) =>
        desdeUltimaTomaCompleta.Select(e => Nivel(e.News2())).DefaultIfEmpty(NivelRespuestaNews2.Cero).Max();

    /// <summary>Frecuencia con la que el paquete propone cada NIC.</summary>
    public static FrecuenciaIntervencion FrecuenciaSugerida(string nic) => nic switch
    {
        ReglasNnn.NicColaboracionMedica => FrecuenciaIntervencion.UnaVez,
        "6490" or "6540" => FrecuenciaIntervencion.CadaTurno,
        _ when Tabla.ContainsKey(nic) => FrecuenciaIntervencion.PorProtocolo,
        _ => FrecuenciaIntervencion.CadaTurno,
    };

    /// <summary>Cada cuánto toca una NIC "por protocolo" en el nivel dado (una NIC fuera de la tabla, por turno).</summary>
    public static Pauta PautaDe(string nic, NivelRespuestaNews2 nivel) =>
        Tabla.TryGetValue(nic, out var pautas) ? pautas[(int)nivel] : Turno;

    /// <summary>Meta NOC por defecto: 4 si la basal es 2 o menos, 5 si es 3 o más; tope 4 en la escala 2 de SpO2 (CE). Nunca menor que la basal.</summary>
    public static int MetaSugerida(int basal, bool topeEscala2 = false)
    {
        var meta = basal <= 2 ? 4 : 5;
        if (topeEscala2)
        {
            meta = Math.Min(meta, 4);
        }

        return Math.Max(meta, basal);
    }

    /// <summary>Dolor de la toma manual (0–10) a la escala NOC del nivel del dolor: 0 → 5, 1–3 → 4, 4–6 → 3, 7–8 → 2, 9–10 → 1 (CE).</summary>
    public static int? NocDesdeDolor(int? dolor) => dolor switch
    {
        null => null,
        0 => 5,
        <= 3 => 4,
        <= 6 => 3,
        <= 8 => 2,
        _ => 1,
    };

    /// <summary>Los parámetros NEWS2 con los que se propone la puntuación global de cada NOC (el peor de ellos).</summary>
    public static IReadOnlyList<ParametroClinico> ParametrosDe(string noc) => noc switch
    {
        "0415" => [ParametroClinico.Fr],
        "0402" => [ParametroClinico.Spo2],
        "0800" => [ParametroClinico.Temperatura],
        "0802" => [ParametroClinico.Fr, ParametroClinico.Spo2, ParametroClinico.Temperatura, ParametroClinico.Pas, ParametroClinico.Fc],
        "0400" => [ParametroClinico.Pas, ParametroClinico.Fc],
        "0601" => [ParametroClinico.Pas],
        "0912" => [ParametroClinico.Conciencia],
        "0703" => [ParametroClinico.Temperatura, ParametroClinico.Fc, ParametroClinico.Fr],
        _ => [],
    };

    /// <summary>
    /// Puntuación NOC (1–5) que se propone a partir de las evaluaciones recientes (la basal del paquete y la valoración del
    /// turno): la mediana de los puntos NEWS2 de cada parámetro en la ventana, y de ellos el peor. Así un valor suelto no
    /// fija la basal. Para el dolor, el de la toma manual. Null si no hay datos: la elige la enfermera.
    /// </summary>
    public static int? PuntuacionDesdeSignos(string noc, IReadOnlyCollection<EvaluacionRiesgo> evaluaciones, int? dolor)
    {
        ArgumentNullException.ThrowIfNull(evaluaciones);
        if (noc == NocDolor)
        {
            return NocDesdeDolor(dolor);
        }

        var peores = ParametrosDe(noc)
            .Select(p => PrecisionSensor.Mediana(evaluaciones.Select(e => e.News2().Puntos.TryGetValue(p, out var x) ? (int?)x : null)))
            .OfType<int>()
            .ToList();
        return peores.Count == 0 ? null : ValoracionNocSugerida.DesdePuntosNews2(peores.Max());
    }

    /// <summary>Ventana de las evaluaciones con que se propone la basal y la valoración del turno.</summary>
    public static readonly TimeSpan VentanaBasal = TimeSpan.FromMinutes(30);

    private static Pauta Horas(int horas) => Pauta.Cada(TimeSpan.FromHours(horas));

    private static Pauta Minutos(int minutos) => Pauta.Cada(TimeSpan.FromMinutes(minutos));
}

public enum EstadoAgenda
{
    /// <summary>Se hizo y todavía no le toca.</summary>
    AlDia = 1,

    /// <summary>Le toca en la próxima hora (o en esta guardia, si es por turno).</summary>
    Proxima = 2,

    /// <summary>Ya le tocaba.</summary>
    Vencida = 3,

    /// <summary>Sin hora fija.</summary>
    SegunNecesidad = 4,
}

/// <summary>Cuándo le toca a una intervención y en qué estado está.</summary>
public sealed record ItemAgenda(Guid IntervencionId, DateTime? ProximaEn, EstadoAgenda Estado);

/// <summary>
/// Agenda de las intervenciones del plan, calculada al leer (como la vigencia de los signos: sin jobs). La NIC 6680 usa el
/// mismo reloj que la toma manual (<see cref="EvaluacionRiesgo.ProximaTomaManualEn"/>), para no duplicar con la alerta
/// "toma pendiente". Una NIC por turno vence una hora antes del fin de la guardia si no se hizo en ella.
/// </summary>
public static class AgendaIntervenciones
{
    /// <summary>Una NIC vencida hace más que esto, con NEWS2 de 5 o más, interrumpe con un push (Fable, CE).</summary>
    public static readonly TimeSpan ToleranciaUrgente = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan Anticipo = TimeSpan.FromHours(1);

    public static ItemAgenda Calcular(
        IntervencionPlan intervencion, DateTime? ultimaEjecucion, NivelRespuestaNews2 nivel, IntervaloTurno guardia,
        DateTime? proximaTomaManual, DateTime ahora)
    {
        ArgumentNullException.ThrowIfNull(intervencion);
        if (!intervencion.Activa)
        {
            return new ItemAgenda(intervencion.Id, null, EstadoAgenda.AlDia);
        }

        var pauta = intervencion.Frecuencia switch
        {
            FrecuenciaIntervencion.CadaTurno => Pauta.Turno,
            FrecuenciaIntervencion.CadaHora => Pauta.Cada(TimeSpan.FromHours(1)),
            FrecuenciaIntervencion.SegunNecesidad => Pauta.Necesidad,
            FrecuenciaIntervencion.PorProtocolo => ProtocoloNnn.PautaDe(intervencion.IntervencionCodigo, nivel),
            _ => Pauta.Cada(TimeSpan.Zero), // UnaVez: le toca desde que se planificó
        };

        if (pauta.SegunNecesidad)
        {
            return new ItemAgenda(intervencion.Id, null, EstadoAgenda.SegunNecesidad);
        }

        DateTime proxima;
        if (pauta.PorTurno)
        {
            var hechaEnLaGuardia = ultimaEjecucion is { } u && u >= guardia.InicioUtc;
            proxima = hechaEnLaGuardia ? guardia.FinUtc : guardia.FinUtc - Anticipo;
        }
        else if (intervencion.Frecuencia == FrecuenciaIntervencion.PorProtocolo
                 && intervencion.IntervencionCodigo == ProtocoloNnn.NicSignosVitales && proximaTomaManual is { } toma)
        {
            proxima = toma;
        }
        else
        {
            proxima = (ultimaEjecucion ?? intervencion.AgregadaEn) + pauta.Intervalo!.Value;
        }

        var estado = ahora >= proxima ? EstadoAgenda.Vencida
            : pauta.PorTurno || proxima - ahora <= Anticipo ? EstadoAgenda.Proxima
            : EstadoAgenda.AlDia;
        if (pauta.PorTurno && ultimaEjecucion is { } hecha && hecha >= guardia.InicioUtc)
        {
            estado = EstadoAgenda.AlDia;
        }

        return new ItemAgenda(intervencion.Id, proxima, estado);
    }
}
