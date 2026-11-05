using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Alertas;

namespace Vitalify.Application.Alertas;

/// <summary>Sección <c>Alertas</c> de la configuración.</summary>
/// <param name="SinSenal">Sin lecturas de un wearable vinculado durante este tiempo → <c>sin-senal</c> (180 s).</param>
/// <param name="Escalamiento">Alta pendiente a los 5 min y media a los 15 min.</param>
/// <param name="IntervaloVigilancia">Cada cuánto corre el vigilante (30 s; 0 lo apaga).</param>
public sealed record OpcionesAlertas(TimeSpan SinSenal, ReglasEscalamiento Escalamiento, TimeSpan IntervaloVigilancia)
{
    public static readonly OpcionesAlertas PorDefecto = new(TimeSpan.FromSeconds(180), ReglasEscalamiento.PorDefecto, TimeSpan.FromSeconds(30));

    /// <summary>
    /// Tras atender una alerta de riesgo, cuánto tiempo no se abre otra salvo que suba el nivel (30 min;
    /// <c>Alertas__MinutosEsperaTrasAtender</c>).
    /// </summary>
    public TimeSpan EsperaTrasAtender { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>Por qué se notifica.</summary>
public static class MotivosNotificacion
{
    public const string Nueva = "nueva";
    public const string Subio = "subio";
    public const string Escalamiento = "escalamiento";
    public const string CambioDeTurno = "cambio-turno";

    /// <summary>Solo sincroniza las pantallas (tomada, atendida, resuelta o valores nuevos): no hace sonar a nadie.</summary>
    public const string Estado = "estado";
}

/// <summary>Textos de la API (kebab-case) para los enums de alertas.</summary>
public static class TextosAlerta
{
    public static string De(TipoAlerta tipo) => tipo switch
    {
        TipoAlerta.Riesgo => "riesgo",
        TipoAlerta.Caida => "caida",
        TipoAlerta.SinSenal => "sin-senal",
        TipoAlerta.TomaPendiente => "toma-pendiente",
        _ => "preocupacion-clinica",
    };

    public static string De(PrioridadAlerta prioridad) => prioridad switch
    {
        PrioridadAlerta.Alta => "alta",
        PrioridadAlerta.Media => "media",
        _ => "baja",
    };

    public static string De(EstadoAlerta estado) => estado switch
    {
        EstadoAlerta.Pendiente => "pendiente",
        EstadoAlerta.EnAtencion => "en-atencion",
        _ => "atendida",
    };

    public static bool TryTipo(string? texto, out TipoAlerta tipo) =>
        TryDe(texto, Enum.GetValues<TipoAlerta>(), De, out tipo);

    public static bool TryEstado(string? texto, out EstadoAlerta estado) =>
        TryDe(texto, Enum.GetValues<EstadoAlerta>(), De, out estado);

    private static bool TryDe<T>(string? texto, IEnumerable<T> valores, Func<T, string> nombre, out T valor)
        where T : struct, Enum
    {
        valor = valores.FirstOrDefault(v => string.Equals(nombre(v), texto?.Trim(), StringComparison.OrdinalIgnoreCase));
        return Enum.IsDefined(valor) && string.Equals(nombre(valor), texto?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Texto de la notificación push: solo la cama, el tipo y el puntaje (por ejemplo "Vitalify · Cama MED-B-01" /
/// "Riesgo alto (NEWS2 7). Toque para atender"). Nunca el nombre, el documento ni el diagnóstico.
/// </summary>
public static class TextosPush
{
    public static string Titulo(string cama) => $"Vitalify · Cama {cama}";

    public static string Cuerpo(AlertaClinica alerta, string motivo)
    {
        var texto = alerta.Tipo switch
        {
            TipoAlerta.Riesgo => $"Riesgo {alerta.NivelRiesgo} ({Puntaje(alerta)}). Toque para atender",
            TipoAlerta.Caida => "Posible caída del paciente. Toque para atender",
            TipoAlerta.SinSenal => "El sensor no envía datos. Revise el wearable",
            TipoAlerta.TomaPendiente => "Toma manual de signos pendiente",
            _ => "Preocupación clínica registrada por enfermería. Toque para revisar",
        };

        return motivo switch
        {
            MotivosNotificacion.Escalamiento => $"Sin atender: {texto}",
            MotivosNotificacion.CambioDeTurno => $"Alerta abierta al iniciar su turno: {texto}",
            _ => texto,
        };
    }

    /// <summary>El puntaje que disparó la alerta: NEWS2, salvo que el nivel lo haya puesto el MEWS.</summary>
    private static string Puntaje(AlertaClinica alerta)
    {
        var porNews2 = AlertaClinica.RangoNivel(alerta.Detalle?.GetValueOrDefault("news2Nivel")) >= AlertaClinica.RangoNivel(alerta.NivelRiesgo);
        return porNews2 || alerta.Mews is null ? $"NEWS2 {alerta.News2}" : $"MEWS {alerta.Mews}";
    }

    /// <summary><c>data</c> del push: <c>alertaId</c>, <c>pacienteId</c> y <c>tipo</c>.</summary>
    public static IReadOnlyDictionary<string, string> Datos(AlertaClinica alerta) => new Dictionary<string, string>
    {
        ["alertaId"] = alerta.Id.ToString(),
        ["pacienteId"] = alerta.PacienteId.ToString(),
        ["tipo"] = TextosAlerta.De(alerta.Tipo),
    };
}

/// <summary>
/// Evento <c>alertaCreada</c>/<c>alertaActualizada</c> de SignalR. Lleva el paciente y la cama, no su nombre: el
/// cliente lo pide a la API con su permiso. <see cref="Destinatarios"/> dice a quién se le pide atención.
/// </summary>
public sealed record AlertaEventoDto(
    Guid AlertaId, string Tipo, string Prioridad, string Estado, Guid PacienteId, Guid HospitalizacionId, Guid ServicioId, string Cama,
    string? NivelRiesgo, int? News2, int? Mews, DateTime CreadaEn, DateTime ActualizadaEn, string Motivo, IReadOnlyList<Guid> Destinatarios)
{
    public static AlertaEventoDto Desde(AlertaClinica a, string cama, string motivo, IReadOnlyCollection<Guid> destinatarios) =>
        new(a.Id, TextosAlerta.De(a.Tipo), TextosAlerta.De(a.Prioridad), TextosAlerta.De(a.Estado), a.PacienteId, a.HospitalizacionId,
            a.ServicioId, cama, a.NivelRiesgo, a.News2, a.Mews, a.CreadaEn, a.ActualizadaEn, motivo, destinatarios.ToList());
}

/// <summary>Evento <c>riesgoActualizado</c>.</summary>
public sealed record RiesgoActualizadoDto(
    Guid PacienteId, Guid HospitalizacionId, string Cama, int News2, int Mews, string Nivel, bool Completo, DateTime EvaluadaEn);

/// <summary>Evento <c>signosActualizados</c> (como máximo uno cada 5 s por paciente).</summary>
public sealed record SignosActualizadosDto(
    Guid PacienteId, Guid HospitalizacionId, string Cama, DateTime MedidoEn, int? Fc, int? Fr, int? Spo2, decimal? Temperatura,
    int? Pas, int? Pad, bool Caida);

/// <summary>Alerta para las consultas, con los tiempos de respuesta (creada → tomada → atendida) en segundos.</summary>
public sealed record AlertaDto(
    Guid Id, string Tipo, string Prioridad, string Estado, Guid PacienteId, Guid HospitalizacionId, Guid ServicioId, string Cama,
    string? NivelRiesgo, int? News2, int? Mews, IReadOnlyDictionary<string, string>? Detalle, DateTime CreadaEn, DateTime ActualizadaEn,
    Guid? TomadaPor, DateTime? TomadaEn, Guid? AtendidaPor, DateTime? AtendidaEn, string? NotaAtencion, DateTime? EscaladaEn,
    double? SegundosHastaTomada, double? SegundosHastaAtendida, int Notificaciones)
{
    public static AlertaDto Desde(AlertaConCama fila)
    {
        var a = fila.Alerta;
        return new AlertaDto(
            a.Id, TextosAlerta.De(a.Tipo), TextosAlerta.De(a.Prioridad), TextosAlerta.De(a.Estado), a.PacienteId, a.HospitalizacionId,
            a.ServicioId, fila.CodigoCama, a.NivelRiesgo, a.News2, a.Mews, a.Detalle, a.CreadaEn, a.ActualizadaEn,
            a.TomadaPor, a.TomadaEn, a.AtendidaPor, a.AtendidaEn, a.NotaAtencion, a.EscaladaEn,
            a.TomadaEn is { } tomada ? Math.Round((tomada - a.CreadaEn).TotalSeconds, 1) : null,
            a.AtendidaEn is { } atendida ? Math.Round((atendida - a.CreadaEn).TotalSeconds, 1) : null,
            a.NotificadaA.Count);
    }
}

public static class ErroresAlerta
{
    public static readonly Error NoEncontrada = Error.NoEncontrado("alerta-no-encontrada", "La alerta no existe.");

    public static readonly Error OtroServicio =
        Error.Prohibido("alerta-de-otro-servicio", "La alerta es de un paciente de otro servicio.");

    public static readonly Error SinServicio =
        Error.Prohibido("usuario-sin-servicio", "Su cuenta no tiene un servicio asignado. Pida al administrador que se lo asigne.");

    public static readonly Error YaAtendida = Error.Conflicto("alerta-ya-atendida", "La alerta ya fue atendida.");

    public static readonly Error TomadaPorOtro = Error.Conflicto("alerta-tomada-por-otro", "Otra persona ya está atendiendo la alerta.");

    /// <summary>Dos disparadores crearon a la vez la misma alerta abierta: se reintenta actualizando la existente.</summary>
    public static readonly Error AbiertaDuplicada = Error.Conflicto("alerta-abierta-duplicada", "Ya hay una alerta abierta de ese tipo.");
}
