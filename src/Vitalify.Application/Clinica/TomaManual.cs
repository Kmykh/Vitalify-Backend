using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Clinica;

/// <summary>
/// Conciencia tal como la envía la app: las respuestas del asistente (ACVPU en cascada) y, opcionalmente, el nivel.
/// Si hay respuestas, el nivel lo calcula el dominio y <b>gana el calculado</b> aunque <see cref="Nivel"/> diga otra
/// cosa. Sin respuestas, se usa <see cref="Nivel"/> (formato de la fase 4).
/// </summary>
public sealed record ConcienciaTomada(
    string? Nivel = null, bool? DespiertoCoherente = null, bool? ConfusionNueva = null, bool? RespondeVoz = null, bool? RespondeDolor = null)
{
    public bool TieneRespuestas => DespiertoCoherente is not null || ConfusionNueva is not null || RespondeVoz is not null || RespondeDolor is not null;

    public bool TieneValor => TieneRespuestas || !string.IsNullOrWhiteSpace(Nivel);

    public RespuestasConciencia Respuestas => new(DespiertoCoherente, ConfusionNueva, RespondeVoz, RespondeDolor);
}

public sealed record OxigenoTomado(bool? Recibe, string? Dispositivo = null, decimal? FlujoLpm = null, int? Fio2 = null, bool? Objetivo88a92 = null);

public sealed record ChecklistPresionTomado(
    bool? Reposo5Min = null, bool? BrazoApoyadoAlturaCorazon = null, bool? BrazaleteTallaCorrecta = null,
    bool? SinCafeTabacoEsfuerzo30Min = null, bool? TensiometroValidado = null);

public sealed record LecturaPresionTomada(int? Pas, int? Pad, DateTime? MedidaEn = null);

/// <param name="Posicion"><c>Sentado</c>, <c>Acostado</c> o <c>DePie</c>.</param>
/// <param name="Brazo"><c>Izquierdo</c> o <c>Derecho</c>.</param>
/// <param name="Metodo"><c>Automatico</c> o <c>Manual</c>.</param>
public sealed record PresionTomada(
    IReadOnlyList<LecturaPresionTomada>? Lecturas, ChecklistPresionTomado? Checklist = null, string? Posicion = null,
    string? Brazo = null, string? Metodo = null);

public sealed record ContextoTomado(int? Dolor = null, bool? AnsiedadOEsfuerzoReciente = null, bool? AntipireticoUltimas4h = null, bool? Betabloqueadores = null);

/// <summary>
/// Una toma manual: el formato del asistente (<see cref="Oxigeno"/>, <see cref="Conciencia"/> con respuestas,
/// <see cref="Presion"/> con lecturas, <see cref="Contexto"/>, <see cref="PreocupacionClinica"/>) y, por
/// compatibilidad, los campos planos de la fase 4 (<see cref="Pas"/>, <see cref="Pad"/>, <see cref="OxigenoSuplementario"/>
/// y el nivel de conciencia como texto).
/// </summary>
public sealed record TomaManualComando
{
    public int? Fr { get; init; }
    public int? Pas { get; init; }
    public int? Pad { get; init; }
    public decimal? Temperatura { get; init; }
    public ConcienciaTomada? Conciencia { get; init; }
    public bool? OxigenoSuplementario { get; init; }
    public OxigenoTomado? Oxigeno { get; init; }
    public PresionTomada? Presion { get; init; }
    public ContextoTomado? Contexto { get; init; }
    public bool? PreocupacionClinica { get; init; }
    public string? VersionGuia { get; init; }

    /// <summary>Momento de la medición; si falta, ahora.</summary>
    public DateTime? ObservadaEn { get; init; }

    public bool TieneAlgunValor =>
        Fr is not null || Pas is not null || Pad is not null || Temperatura is not null || Conciencia?.TieneValor == true
        || OxigenoSuplementario is not null || Oxigeno?.Recibe is not null || Presion?.Lecturas is { Count: > 0 }
        || PreocupacionClinica is not null;
}

/// <summary>
/// Reglas de una toma manual, con un mensaje por campo y los nombres de campo del JSON (<c>presion.lecturas</c>,
/// <c>oxigeno.dispositivo</c>...). Con <c>tomaInicial</c> (vinculación por QR) exige además oxígeno, conciencia y al
/// menos una lectura de presión.
/// </summary>
public class TomaManualValidador : AbstractValidator<TomaManualComando>
{
    public TomaManualValidador(RangosFisiologicos rangos, OpcionesClinicas opciones, IReloj reloj)
        : this(rangos, opciones, reloj, tomaInicial: false)
    {
    }

    protected TomaManualValidador(RangosFisiologicos rangos, OpcionesClinicas opciones, IReloj reloj, bool tomaInicial)
    {
        RuleFor(c => c)
            .Must(c => c.TieneAlgunValor)
            .OverridePropertyName("observacion")
            .WithMessage("Registra al menos un valor: presión, conciencia, oxígeno, fr, temperatura o preocupación clínica.");

        RuleFor(c => c.Fr).Must(v => v is null || rangos.EsValido(VariableSigno.Fr, v.Value))
            .WithMessage($"fr debe estar entre {rangos.Fr}.");
        RuleFor(c => c.Temperatura).Must(v => v is null || rangos.EsValido(VariableSigno.Temperatura, v.Value))
            .WithMessage($"temperatura debe estar entre {rangos.Temperatura} °C.");

        // Presión en el formato plano de la fase 4.
        RuleFor(c => c.Pas)
            .Must((c, pas) => (pas is null) == (c.Pad is null)).WithMessage("La presión necesita pas y pad.")
            .Must((c, pas) => pas is null || c.Pad is null || pas > c.Pad).WithMessage("pas debe ser mayor que pad.")
            .Must(v => v is null || rangos.EsValido(VariableSigno.Pas, v.Value)).WithMessage($"pas debe estar entre {rangos.Pas}.");
        RuleFor(c => c.Pad).Must(v => v is null || rangos.EsValido(VariableSigno.Pad, v.Value))
            .WithMessage($"pad debe estar entre {rangos.Pad}.");

        RuleFor(c => c.Presion)
            .Must((c, p) => p is null || (c.Pas is null && c.Pad is null))
            .WithMessage("Envía la presión como lecturas (presion) o como pas y pad, no de las dos formas.");

        When(c => c.Presion is not null, () => ReglasPresion(rangos, opciones, reloj));
        When(c => c.Conciencia is not null, ReglasConciencia);
        When(c => c.Oxigeno is not null, ReglasOxigeno);

        RuleFor(c => c.Contexto!.Dolor)
            .InclusiveBetween(0, ContextoClinico.DolorMaximo).When(c => c.Contexto?.Dolor is not null)
            .OverridePropertyName("contexto.dolor")
            .WithMessage($"contexto.dolor va de 0 a {ContextoClinico.DolorMaximo}.");

        RuleFor(c => c.VersionGuia).MaximumLength(ObservacionEnfermeria.LargoMaximoVersionGuia)
            .WithMessage($"versionGuia no puede superar {ObservacionEnfermeria.LargoMaximoVersionGuia} caracteres.");

        RuleFor(c => c.ObservadaEn)
            .Must(v => v is null || v.Value.ToUniversalTime() <= reloj.AhoraUtc.AddMinutes(1))
            .WithMessage("observadaEn no puede estar en el futuro.")
            .Must(v => v is null || v.Value.ToUniversalTime() >= reloj.AhoraUtc - FrecuenciaTomaManual.TopeVigencia)
            .WithMessage($"observadaEn no puede tener más de {FrecuenciaTomaManual.TopeVigencia.TotalHours:0} horas: ya no contaría para el puntaje.");

        if (tomaInicial)
        {
            RuleFor(c => c.Oxigeno)
                .Must((c, o) => o?.Recibe is not null || c.OxigenoSuplementario is not null)
                .OverridePropertyName("oxigeno")
                .WithMessage("En la toma inicial es obligatorio indicar si el paciente recibe oxígeno.");
            RuleFor(c => c.Conciencia)
                .Must(c => c is { TieneValor: true })
                .OverridePropertyName("conciencia")
                .WithMessage("En la toma inicial es obligatorio evaluar la conciencia.");
            RuleFor(c => c.Presion)
                .Must((c, p) => p?.Lecturas is { Count: > 0 } || (c.Pas is not null && c.Pad is not null))
                .OverridePropertyName("presion")
                .WithMessage("En la toma inicial es obligatoria al menos una lectura de presión.");
        }
    }

    private void ReglasPresion(RangosFisiologicos rangos, OpcionesClinicas opciones, IReloj reloj)
    {
        RuleFor(c => c.Presion!.Lecturas)
            .Must(l => l is { Count: >= 1 and <= TomaPresion.MaximoLecturas })
            .OverridePropertyName("presion.lecturas")
            .WithMessage($"presion.lecturas debe tener de 1 a {TomaPresion.MaximoLecturas} lecturas.")
            .Must(l => l is null || l.Any(x => x.Pas is null || x.Pad is null) || !TomaPresion.RequiereTerceraLectura(ADominio(l)))
            .WithMessage($"Las dos lecturas difieren en más de {TomaPresion.DiferenciaMaximaMmHg} mmHg (sistólica o diastólica): tome una tercera lectura.");

        // Los errores quedan como presion.lecturas[0].pas (Validacion pasa cada segmento a camelCase).
        RuleForEach(c => c.Presion!.Lecturas)
            .ChildRules(l =>
            {
                l.RuleFor(x => x.Pas).NotNull().WithMessage("pas es obligatoria en cada lectura.")
                    .Must(v => v is null || rangos.EsValido(VariableSigno.Pas, v.Value)).WithMessage($"pas debe estar entre {rangos.Pas}.")
                    .Must((x, pas) => pas is null || x.Pad is null || pas > x.Pad).WithMessage("pas debe ser mayor que pad.");
                l.RuleFor(x => x.Pad).NotNull().WithMessage("pad es obligatoria en cada lectura.")
                    .Must(v => v is null || rangos.EsValido(VariableSigno.Pad, v.Value)).WithMessage($"pad debe estar entre {rangos.Pad}.");
                l.RuleFor(x => x.MedidaEn)
                    .Must(v => v is null || v.Value.ToUniversalTime() <= reloj.AhoraUtc.AddMinutes(1))
                    .WithMessage("medidaEn no puede estar en el futuro.")
                    .Must(v => v is null || v.Value.ToUniversalTime() >= reloj.AhoraUtc - FrecuenciaTomaManual.TopeVigencia)
                    .WithMessage($"medidaEn no puede tener más de {FrecuenciaTomaManual.TopeVigencia.TotalHours:0} horas.");
            });

        RuleFor(c => c.Presion!.Posicion).Must(v => string.IsNullOrWhiteSpace(v) || Enumeraciones.TryParseNombre<PosicionPresion>(v, out _))
            .OverridePropertyName("presion.posicion").WithMessage($"presion.posicion debe ser una de: {Enumeraciones.Nombres<PosicionPresion>()}.");
        RuleFor(c => c.Presion!.Brazo).Must(v => string.IsNullOrWhiteSpace(v) || Enumeraciones.TryParseNombre<BrazoPresion>(v, out _))
            .OverridePropertyName("presion.brazo").WithMessage($"presion.brazo debe ser uno de: {Enumeraciones.Nombres<BrazoPresion>()}.");
        RuleFor(c => c.Presion!.Metodo).Must(v => string.IsNullOrWhiteSpace(v) || Enumeraciones.TryParseNombre<MetodoPresion>(v, out _))
            .OverridePropertyName("presion.metodo").WithMessage($"presion.metodo debe ser uno de: {Enumeraciones.Nombres<MetodoPresion>()}.");
    }

    private void ReglasConciencia()
    {
        RuleFor(c => c.Conciencia!.Nivel)
            .Must(v => string.IsNullOrWhiteSpace(v) || Enumeraciones.TryParseNombre<NivelConciencia>(v, out _))
            .OverridePropertyName("conciencia")
            .WithMessage($"conciencia debe ser una de: {Enumeraciones.Nombres<NivelConciencia>()}.");

        RuleFor(c => c.Conciencia!)
            .Must(c => !c.TieneRespuestas || c.Respuestas.PreguntaPendiente is null)
            .OverridePropertyName("conciencia")
            .WithMessage(c => $"Falta responder conciencia.{c.Conciencia!.Respuestas.PreguntaPendiente} para calcular el nivel ACVPU.");
    }

    private void ReglasOxigeno()
    {
        RuleFor(c => c.Oxigeno!.Recibe).NotNull()
            .OverridePropertyName("oxigeno.recibe")
            .WithMessage("Indica si el paciente recibe oxígeno (oxigeno.recibe).");

        RuleFor(c => c.Oxigeno!.Dispositivo)
            .Must(v => string.IsNullOrWhiteSpace(v) || Enumeraciones.TryParseNombre<DispositivoOxigeno>(v, out _))
            .OverridePropertyName("oxigeno.dispositivo")
            .WithMessage($"oxigeno.dispositivo debe ser uno de: {Enumeraciones.Nombres<DispositivoOxigeno>()}.")
            .Must((c, v) => c.Oxigeno!.Recibe != true || !string.IsNullOrWhiteSpace(v))
            .WithMessage("Indica el dispositivo de oxígeno (oxigeno.dispositivo).");

        RuleFor(c => c.Oxigeno!)
            .Must(o => o.Recibe != false || (string.IsNullOrWhiteSpace(o.Dispositivo) && o.FlujoLpm is null && o.Fio2 is null))
            .OverridePropertyName("oxigeno")
            .WithMessage("Si el paciente no recibe oxígeno, no envíes dispositivo, flujo ni FiO2.");

        RuleFor(c => c.Oxigeno!.FlujoLpm)
            .InclusiveBetween(OxigenoAdministrado.FlujoMinimoLpm, OxigenoAdministrado.FlujoMaximoLpm).When(c => c.Oxigeno!.FlujoLpm is not null)
            .OverridePropertyName("oxigeno.flujoLpm")
            .WithMessage($"oxigeno.flujoLpm debe estar entre {OxigenoAdministrado.FlujoMinimoLpm.ToString(System.Globalization.CultureInfo.InvariantCulture)} y {OxigenoAdministrado.FlujoMaximoLpm} L/min.");

        RuleFor(c => c.Oxigeno!.Fio2)
            .Must((c, v) => v is null || (v is >= OxigenoAdministrado.Fio2Minima and <= OxigenoAdministrado.Fio2Maxima
                                          && OxigenoAdministrado.AdmiteFio2(Dispositivo(c.Oxigeno!.Dispositivo))))
            .OverridePropertyName("oxigeno.fio2")
            .WithMessage($"oxigeno.fio2 va de {OxigenoAdministrado.Fio2Minima} a {OxigenoAdministrado.Fio2Maxima} % y solo se registra con Venturi o AltoFlujo.");
    }

    private static List<LecturaPresion> ADominio(IEnumerable<LecturaPresionTomada> lecturas) =>
        lecturas.Select(l => new LecturaPresion(l.Pas!.Value, l.Pad!.Value, l.MedidaEn?.ToUniversalTime())).ToList();

    internal static DispositivoOxigeno? Dispositivo(string? texto) =>
        Enumeraciones.TryParseNombre<DispositivoOxigeno>(texto, out var d) ? d : null;

    /// <summary>Convierte una toma ya validada en los datos del dominio.</summary>
    internal static DatosObservacion Datos(TomaManualComando c, RangosFisiologicos rangos)
    {
        NivelConciencia? conciencia = c.Conciencia switch
        {
            { TieneRespuestas: true } con => con.Respuestas.Nivel,
            { Nivel: { } nivel } when Enumeraciones.TryParseNombre<NivelConciencia>(nivel, out var n) => n,
            _ => null,
        };

        var presion = c.Presion?.Lecturas is { Count: > 0 } lecturas
            ? TomaPresion.Crear(
                ADominio(lecturas),
                c.Presion.Checklist is { } k
                    ? new ChecklistPresion(k.Reposo5Min == true, k.BrazoApoyadoAlturaCorazon == true, k.BrazaleteTallaCorrecta == true,
                        k.SinCafeTabacoEsfuerzo30Min == true, k.TensiometroValidado == true)
                    : null,
                Enumeraciones.TryParseNombre<PosicionPresion>(c.Presion.Posicion, out var p) ? p : null,
                Enumeraciones.TryParseNombre<BrazoPresion>(c.Presion.Brazo, out var b) ? b : null,
                Enumeraciones.TryParseNombre<MetodoPresion>(c.Presion.Metodo, out var m) ? m : null,
                rangos)
            : null;

        var oxigeno = c.Oxigeno is { Recibe: { } recibe } o
            ? OxigenoAdministrado.Crear(recibe, Dispositivo(o.Dispositivo), o.FlujoLpm, o.Fio2, o.Objetivo88a92)
            : null;

        var contexto = c.Contexto is { } x
            ? ContextoClinico.Crear(x.Dolor, x.AnsiedadOEsfuerzoReciente, x.AntipireticoUltimas4h, x.Betabloqueadores)
            : null;

        return new DatosObservacion
        {
            Fr = c.Fr,
            Pas = c.Pas,
            Pad = c.Pad,
            Presion = presion,
            Temperatura = c.Temperatura,
            Conciencia = conciencia,
            OxigenoSuplementario = c.OxigenoSuplementario,
            Oxigeno = oxigeno,
            Contexto = contexto,
            PreocupacionClinica = c.PreocupacionClinica,
            VersionGuia = c.VersionGuia,
        };
    }
}

/// <summary>Toma inicial de la vinculación por QR: oxígeno, conciencia y presión obligatorios.</summary>
public sealed class TomaInicialValidador(RangosFisiologicos rangos, OpcionesClinicas opciones, IReloj reloj)
    : TomaManualValidador(rangos, opciones, reloj, tomaInicial: true);
