using Vitalify.Domain.Comun;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Domain.Clinica;

/// <summary>
/// Lo que mide el personal clínico en una toma manual. El wearable mide FC, SpO2, temperatura y FR. A mano quedan la
/// presión arterial, el nivel de conciencia (ACVPU) y el oxígeno suplementario. La FR solo se cuenta a mano si el
/// sensor no tiene una vigente, y la temperatura, para confirmarla con un termómetro clínico.
/// </summary>
/// <remarks>
/// <see cref="Presion"/> y <see cref="Oxigeno"/> son el formato del asistente (fase 5). Los campos planos
/// (<see cref="Pas"/>, <see cref="Pad"/>, <see cref="OxigenoSuplementario"/>) se siguen aceptando por compatibilidad;
/// si llegan los dos formatos de oxígeno, gana el del asistente.
/// </remarks>
public sealed record DatosObservacion
{
    public int? Fr { get; init; }
    public int? Pas { get; init; }
    public int? Pad { get; init; }
    public TomaPresion? Presion { get; init; }
    public decimal? Temperatura { get; init; }
    public NivelConciencia? Conciencia { get; init; }
    public bool? OxigenoSuplementario { get; init; }
    public OxigenoAdministrado? Oxigeno { get; init; }
    public ContextoClinico? Contexto { get; init; }
    public bool? PreocupacionClinica { get; init; }
    public string? VersionGuia { get; init; }
}

/// <summary>Una toma manual de signos. Vive en el historial, como las lecturas del wearable.</summary>
public sealed class ObservacionEnfermeria
{
    public const int LargoMaximoVersionGuia = 20;

    public Guid Id { get; private set; }
    public Guid HospitalizacionId { get; private set; }
    public Guid PacienteId { get; private set; }

    /// <summary>Momento en que se tomó la medición.</summary>
    public DateTime ObservadaEn { get; private set; }

    public DateTime RegistradaEn { get; private set; }
    public Guid RegistradaPor { get; private set; }
    public int? Fr { get; private set; }

    /// <summary>Sistólica: la de la toma o, si se tomaron varias lecturas, su promedio redondeado.</summary>
    public int? Pas { get; private set; }

    /// <summary>Diastólica: la de la toma o el promedio redondeado de las lecturas.</summary>
    public int? Pad { get; private set; }

    public decimal? Temperatura { get; private set; }
    public NivelConciencia? Conciencia { get; private set; }
    public bool? OxigenoSuplementario { get; private set; }

    public DispositivoOxigeno? DispositivoOxigeno { get; private set; }
    public decimal? FlujoOxigenoLpm { get; private set; }
    public int? Fio2 { get; private set; }

    /// <summary>¿Tiene indicado un objetivo de SpO2 de 88–92 %? Si sigue en la escala 1, se avisa al médico.</summary>
    public bool? ObjetivoSpo288a92 { get; private set; }

    public PosicionPresion? PosicionPa { get; private set; }
    public BrazoPresion? BrazoPa { get; private set; }
    public MetodoPresion? MetodoPa { get; private set; }
    public ChecklistPresion? ChecklistPa { get; private set; }

    /// <summary>Las lecturas de presión de la toma (de 1 a 3); <see cref="Pas"/> y <see cref="Pad"/> son su promedio.</summary>
    public IReadOnlyList<LecturaPresion>? LecturasPa { get; private set; }

    public ContextoClinico? Contexto { get; private set; }

    /// <summary>"¿Le preocupa este paciente aunque el puntaje sea bajo?" (NEWS2, RCP 2017).</summary>
    public bool? PreocupacionClinica { get; private set; }

    /// <summary>Versión de la guía de toma manual con la que la app armó el asistente.</summary>
    public string? VersionGuia { get; private set; }

    /// <summary>
    /// Hasta cuándo cuentan sus valores para el puntaje: la próxima toma calculada en su evaluación más la tolerancia,
    /// con un tope de 12 h. Null en las tomas anteriores a la fase 6 (valían 4 h).
    /// </summary>
    public DateTime? VigenteHasta { get; private set; }

    private ObservacionEnfermeria()
    {
    }

    /// <summary>Formato plano de la fase 4 (FR, presión, temperatura, conciencia y oxígeno sí/no).</summary>
    public static ObservacionEnfermeria Registrar(
        Guid hospitalizacionId, Guid pacienteId, DateTime observadaEn, DateTime registradaEn, Guid registradaPor,
        int? fr, int? pas, int? pad, decimal? temperatura, NivelConciencia? conciencia, bool? oxigenoSuplementario,
        RangosFisiologicos rangos) =>
        Registrar(hospitalizacionId, pacienteId, observadaEn, registradaEn, registradaPor,
            new DatosObservacion
            {
                Fr = fr, Pas = pas, Pad = pad, Temperatura = temperatura, Conciencia = conciencia,
                OxigenoSuplementario = oxigenoSuplementario,
            },
            rangos);

    public static ObservacionEnfermeria Registrar(
        Guid hospitalizacionId, Guid pacienteId, DateTime observadaEn, DateTime registradaEn, Guid registradaPor,
        DatosObservacion datos, RangosFisiologicos rangos)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(rangos);

        if (datos.Presion is not null && (datos.Pas is not null || datos.Pad is not null))
        {
            throw new ExcepcionDeDominio("Envíe la presión como lecturas o como pas y pad, no de las dos formas.");
        }

        var pas = datos.Presion?.Pas ?? datos.Pas;
        var pad = datos.Presion?.Pad ?? datos.Pad;
        var oxigeno = datos.Oxigeno?.Recibe ?? datos.OxigenoSuplementario;

        if (datos.Fr is null && pas is null && pad is null && datos.Temperatura is null && datos.Conciencia is null
            && oxigeno is null && datos.PreocupacionClinica is null)
        {
            throw new ExcepcionDeDominio("La observación debe tener al menos un valor.");
        }

        if (observadaEn > registradaEn)
        {
            throw new ExcepcionDeDominio("La observación no puede ser posterior a su registro.");
        }

        if ((pas is null) != (pad is null) || (pas is not null && pas <= pad))
        {
            throw new ExcepcionDeDominio("La presión arterial necesita la sistólica y la diastólica, con la sistólica mayor.");
        }

        Validar(VariableSigno.Fr, datos.Fr, rangos);
        Validar(VariableSigno.Pas, pas, rangos);
        Validar(VariableSigno.Pad, pad, rangos);
        Validar(VariableSigno.Temperatura, datos.Temperatura, rangos);
        if (datos.Conciencia is { } c && !Enum.IsDefined(c))
        {
            throw new ExcepcionDeDominio("El nivel de conciencia no es válido.");
        }

        var version = string.IsNullOrWhiteSpace(datos.VersionGuia) ? null : datos.VersionGuia.Trim();
        if (version?.Length > LargoMaximoVersionGuia)
        {
            throw new ExcepcionDeDominio($"La versión de la guía no puede superar {LargoMaximoVersionGuia} caracteres.");
        }

        return new ObservacionEnfermeria
        {
            Id = Guid.NewGuid(),
            HospitalizacionId = hospitalizacionId,
            PacienteId = pacienteId,
            ObservadaEn = observadaEn,
            RegistradaEn = registradaEn,
            RegistradaPor = registradaPor,
            Fr = datos.Fr,
            Pas = pas,
            Pad = pad,
            Temperatura = datos.Temperatura is { } t ? Math.Round(t, 1, MidpointRounding.AwayFromZero) : null,
            Conciencia = datos.Conciencia,
            OxigenoSuplementario = oxigeno,
            DispositivoOxigeno = datos.Oxigeno?.Dispositivo,
            FlujoOxigenoLpm = datos.Oxigeno?.FlujoLpm,
            Fio2 = datos.Oxigeno?.Fio2,
            ObjetivoSpo288a92 = datos.Oxigeno?.Objetivo88a92,
            PosicionPa = datos.Presion?.Posicion,
            BrazoPa = datos.Presion?.Brazo,
            MetodoPa = datos.Presion?.Metodo,
            ChecklistPa = datos.Presion?.Checklist,
            LecturasPa = datos.Presion?.Lecturas,
            Contexto = datos.Contexto,
            PreocupacionClinica = datos.PreocupacionClinica,
            VersionGuia = version,
        };
    }

    public void FijarVigencia(DateTime vigenteHasta) => VigenteHasta = vigenteHasta;

    private static void Validar(VariableSigno variable, decimal? valor, RangosFisiologicos rangos)
    {
        if (valor is { } v && !rangos.EsValido(variable, v))
        {
            throw new ExcepcionDeDominio($"{variable} fuera del rango posible ({rangos.De(variable)}).");
        }
    }
}
