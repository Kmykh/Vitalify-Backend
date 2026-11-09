using System.Text.RegularExpressions;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Nnn;

public enum OrigenDiagnostico
{
    /// <summary>Lo propuso el motor y la enfermera lo confirmó (o lo descartó).</summary>
    Sugerido = 1,

    /// <summary>Lo eligió la enfermera del catálogo.</summary>
    Manual = 2,
}

public enum EstadoDiagnosticoPlan
{
    Activo = 1,
    Resuelto = 2,
    Descartado = 3,
}

public enum FrecuenciaIntervencion
{
    UnaVez = 1,
    CadaTurno = 2,
    CadaHora = 3,
    SegunNecesidad = 4,
}

public enum EstadoSugerencia
{
    Pendiente = 1,
    Confirmada = 2,
    Descartada = 3,

    /// <summary>El motor dejó de proponerla (cambiaron los signos) o el paciente egresó sin que se decidiera.</summary>
    Expirada = 4,
}

/// <summary>
/// Un diagnóstico NANDA-I en el plan de cuidados de una hospitalización. Nada se borra (NTS N° 139-MINSA/2018/DGAIN): un
/// diagnóstico se resuelve o se descarta con motivo, y no vuelve a Activo (si reaparece, se crea otro). Descartar una
/// sugerencia también queda registrado, para medir cuántas acepta la enfermera.
/// </summary>
public sealed partial class DiagnosticoPlan
{
    public const int LargoMinimoMotivo = 5;
    public const int LargoMaximoMotivo = 300;
    public const int LargoMaximoRegla = 10;
    public const int LargoMaximoVersion = 20;
    public const string MotivoEgreso = "Egreso";

    public Guid Id { get; private set; }
    public Guid HospitalizacionId { get; private set; }
    public Guid PacienteId { get; private set; }
    public string DiagnosticoCodigo { get; private set; } = null!;
    public OrigenDiagnostico Origen { get; private set; }

    /// <summary>La regla del motor que lo propuso (por ejemplo <c>R1</c>); null si es manual.</summary>
    public string? ReglaOrigen { get; private set; }

    public Guid? AlertaId { get; private set; }

    /// <summary>Evaluación de riesgo de la que salió la sugerencia (vive en el historial: es un valor, no una clave foránea).</summary>
    public Guid? EvaluacionId { get; private set; }

    public EstadoDiagnosticoPlan Estado { get; private set; }
    public string? MotivoCierre { get; private set; }

    /// <summary>Quién lo confirmó y cuándo; null en una sugerencia descartada (nunca estuvo activo).</summary>
    public Guid? ConfirmadoPor { get; private set; }

    public DateTime? ConfirmadoEn { get; private set; }
    public Guid? CerradoPor { get; private set; }
    public DateTime? CerradoEn { get; private set; }

    /// <summary>Versión de las reglas del motor vigente al registrarlo.</summary>
    public string? VersionReglas { get; private set; }

    private DiagnosticoPlan()
    {
    }

    public bool EstaActivo => Estado == EstadoDiagnosticoPlan.Activo;

    /// <summary>La enfermera confirma un diagnóstico (sugerido o elegido del catálogo): queda Activo.</summary>
    public static DiagnosticoPlan Confirmar(
        Guid hospitalizacionId, Guid pacienteId, string diagnosticoCodigo, OrigenDiagnostico origen, string? reglaOrigen,
        Guid? alertaId, Guid? evaluacionId, string? versionReglas, Guid enfermeraId, DateTime ahora)
    {
        var d = Nuevo(hospitalizacionId, pacienteId, diagnosticoCodigo, origen, reglaOrigen, alertaId, evaluacionId, versionReglas);
        d.Estado = EstadoDiagnosticoPlan.Activo;
        d.ConfirmadoPor = enfermeraId;
        d.ConfirmadoEn = ahora;
        return d;
    }

    /// <summary>La enfermera descarta una sugerencia del motor, con motivo obligatorio.</summary>
    public static DiagnosticoPlan DescartarSugerencia(
        Guid hospitalizacionId, Guid pacienteId, string diagnosticoCodigo, string reglaOrigen, Guid? alertaId, Guid? evaluacionId,
        string versionReglas, string motivo, Guid enfermeraId, DateTime ahora)
    {
        var d = Nuevo(hospitalizacionId, pacienteId, diagnosticoCodigo, OrigenDiagnostico.Sugerido, reglaOrigen, alertaId, evaluacionId, versionReglas);
        d.Estado = EstadoDiagnosticoPlan.Descartado;
        d.MotivoCierre = Motivo(motivo);
        d.CerradoPor = enfermeraId;
        d.CerradoEn = ahora;
        return d;
    }

    /// <summary>Resuelve o descarta un diagnóstico activo. No se puede volver a Activo.</summary>
    public void Cerrar(EstadoDiagnosticoPlan estado, string motivo, Guid usuarioId, DateTime ahora)
    {
        if (estado == EstadoDiagnosticoPlan.Activo || !Enum.IsDefined(estado))
        {
            throw new ExcepcionDeDominio("Un diagnóstico solo se cierra como Resuelto o Descartado; si reaparece, se registra uno nuevo.");
        }

        if (!EstaActivo)
        {
            throw new ExcepcionDeDominio("El diagnóstico ya está cerrado.");
        }

        MotivoCierre = Motivo(motivo);
        Estado = estado;
        CerradoPor = usuarioId;
        CerradoEn = ahora;
    }

    /// <summary>Al egresar la hospitalización, los diagnósticos activos quedan Resueltos con el motivo "Egreso".</summary>
    public void ResolverPorEgreso(Guid usuarioId, DateTime ahora) => Cerrar(EstadoDiagnosticoPlan.Resuelto, MotivoEgreso, usuarioId, ahora);

    public static bool EsReglaValida(string? regla) => regla is not null && RegexRegla().IsMatch(regla);

    private static DiagnosticoPlan Nuevo(
        Guid hospitalizacionId, Guid pacienteId, string diagnosticoCodigo, OrigenDiagnostico origen, string? reglaOrigen,
        Guid? alertaId, Guid? evaluacionId, string? versionReglas)
    {
        if (!CodigosNnn.EsDiagnostico(diagnosticoCodigo))
        {
            throw new ExcepcionDeDominio("El diagnóstico debe ser un código NANDA-I de 5 dígitos.");
        }

        if (!Enum.IsDefined(origen))
        {
            throw new ExcepcionDeDominio("El origen del diagnóstico no es válido.");
        }

        if (origen == OrigenDiagnostico.Sugerido && !EsReglaValida(reglaOrigen))
        {
            throw new ExcepcionDeDominio("Un diagnóstico sugerido debe indicar la regla que lo propuso (por ejemplo R1).");
        }

        if (versionReglas is { Length: > LargoMaximoVersion })
        {
            throw new ExcepcionDeDominio("La versión de las reglas es demasiado larga.");
        }

        return new DiagnosticoPlan
        {
            Id = Guid.NewGuid(),
            HospitalizacionId = hospitalizacionId,
            PacienteId = pacienteId,
            DiagnosticoCodigo = diagnosticoCodigo,
            Origen = origen,
            ReglaOrigen = origen == OrigenDiagnostico.Sugerido ? reglaOrigen : null,
            AlertaId = alertaId,
            EvaluacionId = evaluacionId,
            VersionReglas = versionReglas,
        };
    }

    private static string Motivo(string? motivo)
    {
        var limpio = motivo?.Trim() ?? string.Empty;
        return limpio.Length is >= LargoMinimoMotivo and <= LargoMaximoMotivo
            ? limpio
            : throw new ExcepcionDeDominio($"El motivo es obligatorio y debe tener entre {LargoMinimoMotivo} y {LargoMaximoMotivo} caracteres.");
    }

    [GeneratedRegex("^R[0-9]{1,3}$")]
    private static partial Regex RegexRegla();
}

/// <summary>Una intervención NIC planificada para un diagnóstico, con su frecuencia.</summary>
public sealed class IntervencionPlan
{
    public Guid Id { get; private set; }
    public Guid PlanDiagnosticoId { get; private set; }
    public string IntervencionCodigo { get; private set; } = null!;
    public FrecuenciaIntervencion Frecuencia { get; private set; }

    /// <summary>Una intervención de una sola vez deja de estar activa al ejecutarse.</summary>
    public bool Activa { get; private set; }

    public Guid AgregadaPor { get; private set; }
    public DateTime AgregadaEn { get; private set; }

    private IntervencionPlan()
    {
    }

    public static IntervencionPlan Planificar(Guid planDiagnosticoId, string intervencionCodigo, FrecuenciaIntervencion frecuencia, Guid usuarioId, DateTime ahora)
    {
        if (!CodigosNnn.EsIntervencion(intervencionCodigo))
        {
            throw new ExcepcionDeDominio("La intervención debe ser un código NIC de 4 dígitos.");
        }

        if (!Enum.IsDefined(frecuencia))
        {
            throw new ExcepcionDeDominio("La frecuencia no es válida.");
        }

        return new IntervencionPlan
        {
            Id = Guid.NewGuid(),
            PlanDiagnosticoId = planDiagnosticoId,
            IntervencionCodigo = intervencionCodigo,
            Frecuencia = frecuencia,
            Activa = true,
            AgregadaPor = usuarioId,
            AgregadaEn = ahora,
        };
    }

    /// <summary>Registra lo que se hizo. La observación es texto clínico: nunca va a los logs.</summary>
    public EjecucionIntervencion Ejecutar(Guid usuarioId, DateTime ahora, Guid? alertaId, string? observacion)
    {
        if (!Activa)
        {
            throw new ExcepcionDeDominio("La intervención ya no está activa.");
        }

        var ejecucion = EjecucionIntervencion.Registrar(Id, usuarioId, ahora, alertaId, observacion);
        if (Frecuencia == FrecuenciaIntervencion.UnaVez)
        {
            Activa = false;
        }

        return ejecucion;
    }
}

/// <summary>Una ejecución de una intervención: quién, cuándo, por qué alerta (si la hubo) y una observación opcional.</summary>
public sealed class EjecucionIntervencion
{
    public const int LargoMaximoObservacion = 500;

    public Guid Id { get; private set; }
    public Guid PlanIntervencionId { get; private set; }
    public Guid EjecutadaPor { get; private set; }
    public DateTime EjecutadaEn { get; private set; }
    public Guid? AlertaId { get; private set; }
    public string? Observacion { get; private set; }

    private EjecucionIntervencion()
    {
    }

    internal static EjecucionIntervencion Registrar(Guid planIntervencionId, Guid usuarioId, DateTime ahora, Guid? alertaId, string? observacion)
    {
        var texto = string.IsNullOrWhiteSpace(observacion) ? null : observacion.Trim();
        if (texto is { Length: > LargoMaximoObservacion })
        {
            throw new ExcepcionDeDominio($"La observación tiene como máximo {LargoMaximoObservacion} caracteres.");
        }

        return new EjecucionIntervencion
        {
            Id = Guid.NewGuid(),
            PlanIntervencionId = planIntervencionId,
            EjecutadaPor = usuarioId,
            EjecutadaEn = ahora,
            AlertaId = alertaId,
            Observacion = texto,
        };
    }
}

/// <summary>Un resultado NOC esperado para un diagnóstico: puntuación basal y meta en la escala de 1 a 5.</summary>
public sealed class ResultadoPlan
{
    public Guid Id { get; private set; }
    public Guid PlanDiagnosticoId { get; private set; }
    public string ResultadoCodigo { get; private set; } = null!;
    public int PuntuacionBasal { get; private set; }
    public int PuntuacionMeta { get; private set; }

    private ResultadoPlan()
    {
    }

    public static ResultadoPlan Planificar(Guid planDiagnosticoId, string resultadoCodigo, int puntuacionBasal, int puntuacionMeta)
    {
        if (!CodigosNnn.EsResultado(resultadoCodigo))
        {
            throw new ExcepcionDeDominio("El resultado debe ser un código NOC de 4 dígitos.");
        }

        if (!ValoracionNocSugerida.EsValida(puntuacionBasal) || !ValoracionNocSugerida.EsValida(puntuacionMeta))
        {
            throw new ExcepcionDeDominio("Las puntuaciones NOC van de 1 a 5.");
        }

        if (puntuacionMeta < puntuacionBasal)
        {
            throw new ExcepcionDeDominio("La puntuación meta debe ser mayor o igual que la basal.");
        }

        return new ResultadoPlan
        {
            Id = Guid.NewGuid(),
            PlanDiagnosticoId = planDiagnosticoId,
            ResultadoCodigo = resultadoCodigo,
            PuntuacionBasal = puntuacionBasal,
            PuntuacionMeta = puntuacionMeta,
        };
    }
}

/// <summary>
/// Una valoración NOC (1 a 5) de un indicador del resultado, o del resultado completo si <see cref="IndicadorCodigo"/> es
/// null. No se edita: una valoración mal hecha se corrige con otra nueva (NTS 139).
/// </summary>
public sealed class ValoracionResultado
{
    public const int LargoMaximoTurno = 40;

    public Guid Id { get; private set; }
    public Guid PlanResultadoId { get; private set; }
    public string? IndicadorCodigo { get; private set; }
    public int Puntuacion { get; private set; }

    /// <summary>Lo que propuso el motor a partir de los puntos NEWS2 (solo en los indicadores vitales).</summary>
    public int? PuntuacionSugerida { get; private set; }

    public Guid ValoradaPor { get; private set; }
    public DateTime ValoradaEn { get; private set; }

    /// <summary>Guardia en la que se valoró, por ejemplo <c>2026-11-09/GuardiaDiurna</c>.</summary>
    public string? Turno { get; private set; }

    private ValoracionResultado()
    {
    }

    public static ValoracionResultado Registrar(
        Guid planResultadoId, string? indicadorCodigo, int puntuacion, int? puntuacionSugerida, Guid usuarioId, DateTime ahora, string? turno)
    {
        if (indicadorCodigo is not null && !CodigosNnn.EsIndicador(indicadorCodigo))
        {
            throw new ExcepcionDeDominio("El indicador debe ser un código NOC de 6 dígitos.");
        }

        if (!ValoracionNocSugerida.EsValida(puntuacion) || puntuacionSugerida is { } s && !ValoracionNocSugerida.EsValida(s))
        {
            throw new ExcepcionDeDominio("Las puntuaciones NOC van de 1 a 5.");
        }

        if (turno is { Length: > LargoMaximoTurno })
        {
            throw new ExcepcionDeDominio("El turno es demasiado largo.");
        }

        return new ValoracionResultado
        {
            Id = Guid.NewGuid(),
            PlanResultadoId = planResultadoId,
            IndicadorCodigo = indicadorCodigo,
            Puntuacion = puntuacion,
            PuntuacionSugerida = puntuacionSugerida,
            ValoradaPor = usuarioId,
            ValoradaEn = ahora,
            Turno = turno,
        };
    }
}

/// <summary>
/// Registro de cada sugerencia que el motor emitió para una hospitalización y qué pasó con ella. Sirve para medir la tasa
/// de aceptación por regla (capítulo de validación de la tesis). Hay como máximo una pendiente por diagnóstico.
/// </summary>
public sealed class SugerenciaEmitida
{
    public Guid Id { get; private set; }
    public Guid HospitalizacionId { get; private set; }
    public Guid PacienteId { get; private set; }
    public string DiagnosticoCodigo { get; private set; } = null!;
    public string Regla { get; private set; } = null!;
    public string VersionReglas { get; private set; } = null!;
    public PrioridadSugerencia Prioridad { get; private set; }
    public Guid? EvaluacionId { get; private set; }
    public Guid? AlertaId { get; private set; }
    public DateTime EmitidaEn { get; private set; }
    public EstadoSugerencia Estado { get; private set; }
    public DateTime? ResueltaEn { get; private set; }
    public Guid? PlanDiagnosticoId { get; private set; }

    private SugerenciaEmitida()
    {
    }

    public static SugerenciaEmitida Emitir(Guid hospitalizacionId, Guid pacienteId, SugerenciaNnn sugerencia, string versionReglas, DateTime ahora)
    {
        ArgumentNullException.ThrowIfNull(sugerencia);
        return new SugerenciaEmitida
        {
            Id = Guid.NewGuid(),
            HospitalizacionId = hospitalizacionId,
            PacienteId = pacienteId,
            DiagnosticoCodigo = sugerencia.DiagnosticoCodigo,
            Regla = sugerencia.Regla,
            VersionReglas = versionReglas,
            Prioridad = sugerencia.Prioridad,
            EvaluacionId = sugerencia.EvaluacionId,
            AlertaId = sugerencia.AlertaId,
            EmitidaEn = ahora,
            Estado = EstadoSugerencia.Pendiente,
        };
    }

    public bool EstaPendiente => Estado == EstadoSugerencia.Pendiente;

    public void Confirmar(Guid planDiagnosticoId, DateTime ahora) => Resolver(EstadoSugerencia.Confirmada, ahora, planDiagnosticoId);

    public void Descartar(Guid planDiagnosticoId, DateTime ahora) => Resolver(EstadoSugerencia.Descartada, ahora, planDiagnosticoId);

    public void Expirar(DateTime ahora) => Resolver(EstadoSugerencia.Expirada, ahora, null);

    private void Resolver(EstadoSugerencia estado, DateTime ahora, Guid? planDiagnosticoId)
    {
        if (!EstaPendiente)
        {
            throw new ExcepcionDeDominio("La sugerencia ya se resolvió.");
        }

        Estado = estado;
        ResueltaEn = ahora;
        PlanDiagnosticoId = planDiagnosticoId;
    }
}
