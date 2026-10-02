namespace Vitalify.Domain.Clinica;

public enum OrigenEvaluacion
{
    /// <summary>La disparó una lectura del wearable.</summary>
    Lectura = 1,

    /// <summary>La disparó una observación registrada por el personal clínico.</summary>
    Observacion = 2,
}

/// <summary>
/// Cálculo de NEWS2 y MEWS en un momento dado, con los valores que se usaron. Se guarda en el historial para ver la
/// tendencia; el desglose por parámetro se recalcula al leer a partir de los valores (las tablas son deterministas).
/// </summary>
public sealed class EvaluacionRiesgo
{
    public Guid Id { get; private set; }
    public Guid HospitalizacionId { get; private set; }
    public Guid PacienteId { get; private set; }
    public DateTime EvaluadaEn { get; private set; }
    public OrigenEvaluacion Origen { get; private set; }

    public int? Fc { get; private set; }
    public int? Spo2 { get; private set; }
    public decimal? Temperatura { get; private set; }
    public int? Fr { get; private set; }
    public int? Pas { get; private set; }
    public NivelConciencia? Conciencia { get; private set; }
    public bool? OxigenoSuplementario { get; private set; }
    public EscalaSpo2 EscalaSpo2 { get; private set; }

    public int News2Total { get; private set; }
    public NivelRiesgoNews2 News2Nivel { get; private set; }
    public bool News2Completo { get; private set; }
    public int MewsTotal { get; private set; }
    public NivelRiesgoMews MewsNivel { get; private set; }
    public bool MewsCompleto { get; private set; }

    private EvaluacionRiesgo()
    {
    }

    public ParametrosClinicos Parametros => new(Fr, Spo2, OxigenoSuplementario, Pas, Fc, Conciencia, Temperatura);

    public static EvaluacionRiesgo Calcular(
        Guid hospitalizacionId, Guid pacienteId, DateTime evaluadaEn, OrigenEvaluacion origen, ParametrosClinicos parametros,
        EscalaSpo2 escala = EscalaSpo2.Escala1)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        var news2 = CalculadoraNews2.Calcular(parametros, escala);
        var mews = CalculadoraMews.Calcular(parametros);

        return new EvaluacionRiesgo
        {
            Id = Guid.NewGuid(),
            HospitalizacionId = hospitalizacionId,
            PacienteId = pacienteId,
            EvaluadaEn = evaluadaEn,
            Origen = origen,
            Fc = parametros.Fc,
            Spo2 = parametros.Spo2,
            Temperatura = parametros.Temperatura,
            Fr = parametros.Fr,
            Pas = parametros.Pas,
            Conciencia = parametros.Conciencia,
            OxigenoSuplementario = parametros.OxigenoSuplementario,
            EscalaSpo2 = escala,
            News2Total = news2.Total,
            News2Nivel = news2.Nivel,
            News2Completo = news2.Completo,
            MewsTotal = mews.Total,
            MewsNivel = mews.Nivel,
            MewsCompleto = mews.Completo,
        };
    }

    public ResultadoNews2 News2() => CalculadoraNews2.Calcular(Parametros, EscalaSpo2);

    public ResultadoMews Mews() => CalculadoraMews.Calcular(Parametros);
}
