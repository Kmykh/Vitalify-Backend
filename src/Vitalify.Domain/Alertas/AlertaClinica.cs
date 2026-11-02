using Vitalify.Domain.Clinica;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Alertas;

public enum TipoAlerta
{
    /// <summary>NEWS2 bajo-medio, medio o alto (o MEWS medio o alto).</summary>
    Riesgo = 1,

    /// <summary>El wearable detectó una caída.</summary>
    Caida = 2,

    /// <summary>Wearable vinculado sin lecturas (180 s) o desconectado del broker.</summary>
    SinSenal = 3,

    /// <summary>Venció el plazo de la toma manual de signos.</summary>
    TomaPendiente = 4,

    /// <summary>La enfermera registró preocupación clínica.</summary>
    PreocupacionClinica = 5,
}

public enum PrioridadAlerta
{
    Baja = 1,
    Media = 2,
    Alta = 3,
}

public enum EstadoAlerta
{
    Pendiente = 1,
    EnAtencion = 2,
    Atendida = 3,
}

/// <summary>Una notificación enviada por una alerta: a quién, por qué canal, por qué motivo, cuándo y con qué resultado.</summary>
/// <param name="Canal"><c>tiempo-real</c> (SignalR) o <c>push</c>.</param>
/// <param name="Motivo"><c>nueva</c>, <c>subio</c>, <c>escalamiento</c> o <c>cambio-turno</c>.</param>
/// <param name="Resultado">Por ejemplo <c>conectado</c>, <c>sin-conexion</c>, <c>enviado</c>, <c>sin-dispositivo</c>, <c>no-registrado</c> o <c>error</c>.</param>
public sealed record NotificacionAlerta(Guid UsuarioId, string Canal, string Motivo, DateTime En, string Resultado);

/// <summary>Tiempos de escalamiento (<c>Alertas__Escalamiento__*</c>). La prioridad baja no escala.</summary>
public sealed record ReglasEscalamiento(TimeSpan Alta, TimeSpan Media)
{
    public static readonly ReglasEscalamiento PorDefecto = new(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));
}

/// <summary>
/// Alerta clínica de una hospitalización. Hay como máximo una abierta (pendiente o en atención) por tipo: si el
/// disparador se repite, se actualiza la misma alerta. Las de riesgo, caída y preocupación clínica las cierra siempre
/// una persona; las de sin señal y toma pendiente se resuelven solas cuando vuelve la señal o llega la toma.
/// </summary>
public sealed class AlertaClinica
{
    public const int LargoMinimoNota = 5;
    public const int LargoMaximoNota = 1000;
    public const string NotaResolucionAutomatica = "Resuelta automáticamente";

    private List<NotificacionAlerta> _notificadaA = [];

    public Guid Id { get; private set; }
    public Guid HospitalizacionId { get; private set; }
    public Guid PacienteId { get; private set; }
    public Guid ServicioId { get; private set; }
    public TipoAlerta Tipo { get; private set; }
    public PrioridadAlerta Prioridad { get; private set; }
    public EstadoAlerta Estado { get; private set; }

    /// <summary>Solo en las de riesgo: <c>bajo-medio</c>, <c>medio</c> o <c>alto</c>.</summary>
    public string? NivelRiesgo { get; private set; }

    public int? News2 { get; private set; }
    public int? Mews { get; private set; }

    /// <summary>Datos técnicos del disparador (nunca nombre, documento ni diagnóstico).</summary>
    public IReadOnlyDictionary<string, string>? Detalle { get; private set; }

    public DateTime CreadaEn { get; private set; }
    public DateTime ActualizadaEn { get; private set; }
    public Guid? TomadaPor { get; private set; }
    public DateTime? TomadaEn { get; private set; }
    public Guid? AtendidaPor { get; private set; }
    public DateTime? AtendidaEn { get; private set; }
    public string? NotaAtencion { get; private set; }
    public DateTime? EscaladaEn { get; private set; }

    public IReadOnlyList<NotificacionAlerta> NotificadaA
    {
        get => _notificadaA;
        private set => _notificadaA = value.ToList();
    }

    private AlertaClinica()
    {
    }

    public bool EstaAbierta => Estado != EstadoAlerta.Atendida;

    /// <summary>Solo las de sin señal y toma pendiente se resuelven sin intervención de una persona.</summary>
    public static bool SeResuelveSola(TipoAlerta tipo) => tipo is TipoAlerta.SinSenal or TipoAlerta.TomaPendiente;

    public static AlertaClinica Crear(
        Guid hospitalizacionId, Guid pacienteId, Guid servicioId, TipoAlerta tipo, PrioridadAlerta prioridad,
        string? nivelRiesgo, int? news2, int? mews, IReadOnlyDictionary<string, string>? detalle, DateTime ahora)
    {
        if (!Enum.IsDefined(tipo) || !Enum.IsDefined(prioridad))
        {
            throw new ExcepcionDeDominio("El tipo o la prioridad de la alerta no son válidos.");
        }

        return new AlertaClinica
        {
            Id = Guid.NewGuid(),
            HospitalizacionId = hospitalizacionId,
            PacienteId = pacienteId,
            ServicioId = servicioId,
            Tipo = tipo,
            Prioridad = prioridad,
            Estado = EstadoAlerta.Pendiente,
            NivelRiesgo = nivelRiesgo,
            News2 = news2,
            Mews = mews,
            Detalle = detalle,
            CreadaEn = ahora,
            ActualizadaEn = ahora,
        };
    }

    /// <summary>
    /// El disparador se repitió con valores nuevos. Siempre se guardan los valores; devuelve true solo si
    /// <b>subió</b> la prioridad o el nivel de riesgo (entonces hay que volver a notificar).
    /// </summary>
    public bool Actualizar(PrioridadAlerta prioridad, string? nivelRiesgo, int? news2, int? mews, IReadOnlyDictionary<string, string>? detalle, DateTime ahora)
    {
        ExigirAbierta();
        var subio = prioridad > Prioridad || RangoNivel(nivelRiesgo) > RangoNivel(NivelRiesgo);
        if (subio)
        {
            Prioridad = (PrioridadAlerta)Math.Max((int)Prioridad, (int)prioridad);
            NivelRiesgo = RangoNivel(nivelRiesgo) > RangoNivel(NivelRiesgo) ? nivelRiesgo : NivelRiesgo;
        }

        News2 = news2 ?? News2;
        Mews = mews ?? Mews;
        Detalle = detalle ?? Detalle;
        ActualizadaEn = ahora;
        return subio;
    }

    /// <summary>¿Cambiarían los valores? Para no guardar en cada lectura lo mismo.</summary>
    public bool Cambiaria(PrioridadAlerta prioridad, string? nivelRiesgo, int? news2, int? mews) =>
        prioridad > Prioridad || RangoNivel(nivelRiesgo) > RangoNivel(NivelRiesgo) || (news2 is not null && news2 != News2) || (mews is not null && mews != Mews);

    /// <summary>Una enfermera o un médico la toma: pasa a en atención.</summary>
    public void Tomar(Guid usuarioId, DateTime ahora)
    {
        if (Estado == EstadoAlerta.Atendida)
        {
            throw new ExcepcionDeDominio("La alerta ya fue atendida.");
        }

        if (Estado == EstadoAlerta.EnAtencion && TomadaPor != usuarioId)
        {
            throw new ExcepcionDeDominio("Otra persona ya está atendiendo la alerta.");
        }

        if (Estado == EstadoAlerta.EnAtencion)
        {
            return;
        }

        Estado = EstadoAlerta.EnAtencion;
        TomadaPor = usuarioId;
        TomadaEn = ahora;
        ActualizadaEn = ahora;
    }

    /// <summary>La cierra una persona con una nota. Si no la había tomado, cuenta como tomada en ese momento.</summary>
    public void Atender(Guid usuarioId, string nota, DateTime ahora)
    {
        ExigirAbierta();
        var notaLimpia = nota?.Trim() ?? string.Empty;
        if (notaLimpia.Length is < LargoMinimoNota or > LargoMaximoNota)
        {
            throw new ExcepcionDeDominio($"La nota de atención debe tener entre {LargoMinimoNota} y {LargoMaximoNota} caracteres.");
        }

        TomadaPor ??= usuarioId;
        TomadaEn ??= ahora;
        Estado = EstadoAlerta.Atendida;
        AtendidaPor = usuarioId;
        AtendidaEn = ahora;
        NotaAtencion = notaLimpia;
        ActualizadaEn = ahora;
    }

    /// <summary>Volvió la señal o llegó la toma: solo para <see cref="TipoAlerta.SinSenal"/> y <see cref="TipoAlerta.TomaPendiente"/>.</summary>
    public void ResolverAutomaticamente(DateTime ahora)
    {
        if (!SeResuelveSola(Tipo))
        {
            throw new ExcepcionDeDominio($"Una alerta de tipo {Tipo} la cierra siempre una persona.");
        }

        ExigirAbierta();
        Estado = EstadoAlerta.Atendida;
        AtendidaEn = ahora;
        NotaAtencion = NotaResolucionAutomatica;
        ActualizadaEn = ahora;
    }

    /// <summary>Alta pendiente tras <see cref="ReglasEscalamiento.Alta"/> o media tras <see cref="ReglasEscalamiento.Media"/>; una sola vez.</summary>
    public bool DebeEscalar(DateTime ahora, ReglasEscalamiento reglas)
    {
        ArgumentNullException.ThrowIfNull(reglas);
        if (Estado != EstadoAlerta.Pendiente || EscaladaEn is not null)
        {
            return false;
        }

        return Prioridad switch
        {
            PrioridadAlerta.Alta => ahora - CreadaEn >= reglas.Alta,
            PrioridadAlerta.Media => ahora - CreadaEn >= reglas.Media,
            _ => false,
        };
    }

    public void MarcarEscalada(DateTime ahora)
    {
        EscaladaEn ??= ahora;
        ActualizadaEn = ahora;
    }

    public void RegistrarNotificaciones(IEnumerable<NotificacionAlerta> notificaciones)
    {
        // Se reemplaza la lista para que EF detecte el cambio de la columna jsonb.
        _notificadaA = [.. _notificadaA, .. notificaciones];
    }

    /// <summary>bajo-medio = 1, medio = 2, alto = 3; cualquier otro, 0.</summary>
    public static int RangoNivel(string? nivel) => nivel switch
    {
        "bajo-medio" => 1,
        "medio" => 2,
        "alto" => 3,
        _ => 0,
    };

    private void ExigirAbierta()
    {
        if (!EstaAbierta)
        {
            throw new ExcepcionDeDominio("La alerta ya fue atendida.");
        }
    }
}

/// <summary>Prioridad de una alerta de riesgo según NEWS2 y MEWS: se toma la mayor.</summary>
public static class PrioridadRiesgo
{
    /// <returns>null si ni NEWS2 ni MEWS justifican una alerta (NEWS2 bajo y MEWS bajo).</returns>
    public static (PrioridadAlerta Prioridad, string Nivel)? Calcular(NivelRiesgoNews2 news2, NivelRiesgoMews mews)
    {
        var porNews2 = news2 switch
        {
            NivelRiesgoNews2.Alto => (PrioridadAlerta.Alta, "alto"),
            NivelRiesgoNews2.Medio => (PrioridadAlerta.Media, "medio"),
            NivelRiesgoNews2.BajoMedio => (PrioridadAlerta.Baja, "bajo-medio"),
            _ => ((PrioridadAlerta Prioridad, string Nivel)?)null,
        };

        var porMews = mews switch
        {
            NivelRiesgoMews.Alto => (PrioridadAlerta.Alta, "alto"),
            NivelRiesgoMews.Medio => (PrioridadAlerta.Media, "medio"),
            _ => ((PrioridadAlerta Prioridad, string Nivel)?)null,
        };

        return (porNews2, porMews) switch
        {
            (null, null) => null,
            ({ } a, null) => a,
            (null, { } b) => b,
            ({ } a, { } b) => a.Prioridad >= b.Prioridad ? a : b,
        };
    }
}
