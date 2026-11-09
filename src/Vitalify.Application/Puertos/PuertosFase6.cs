using Vitalify.Application.Alertas;
using Vitalify.Application.Comun;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Notificaciones;
using Vitalify.Domain.Servicios;
using Vitalify.Domain.Turnos;

namespace Vitalify.Application.Puertos;

public interface IRepositorioServicios
{
    Task<Servicio?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default);

    Task<IReadOnlyList<Servicio>> ListarAsync(CancellationToken ct = default);

    void Agregar(Servicio servicio);
}

/// <summary>Programación mensual de turnos. Se guarda con <see cref="IUnidadDeTrabajo"/>.</summary>
public interface IRepositorioProgramacion
{
    /// <summary>Turnos del servicio que empiezan entre ambas fechas locales (inclusive).</summary>
    Task<IReadOnlyList<ProgramacionTurno>> ListarAsync(Guid servicioId, DateOnly desde, DateOnly hasta, CancellationToken ct = default);

    Task<IReadOnlyList<ProgramacionTurno>> ListarDeUsuarioAsync(Guid usuarioId, DateOnly desde, DateOnly hasta, CancellationToken ct = default);

    /// <summary>Borra la programación del mes del servicio y agrega la nueva (se confirma al guardar).</summary>
    Task ReemplazarMesAsync(Guid servicioId, int anio, int mes, IReadOnlyList<ProgramacionTurno> nuevos, CancellationToken ct = default);
}

/// <param name="EsRespaldo">No había programación para ese momento: son todas las enfermeras activas del servicio (o de todos).</param>
public sealed record EnfermerasDeTurno(IReadOnlyList<Guid> Enfermeras, bool EsRespaldo);

/// <summary>Quién está de turno. El adaptador escribe en el log (sin nombres) cuando usa el respaldo.</summary>
public interface ITurnoVigente
{
    Task<EnfermerasDeTurno> EnfermerasDeTurnoAsync(Guid servicioId, DateTime instante, CancellationToken ct = default);
}

public sealed record FiltroAlertas(IReadOnlyCollection<EstadoAlerta> Estados, Guid ServicioId, TipoAlerta? Tipo, DateTime? Desde, DateTime? Hasta);

/// <summary>Alerta con el código de la cama (para las consultas y los eventos).</summary>
public sealed record AlertaConCama(AlertaClinica Alerta, string CodigoCama);

/// <summary>Alertas clínicas (esquema <c>vitalify</c>). Se guardan con <see cref="IUnidadDeTrabajo"/>.</summary>
public interface IRepositorioAlertas
{
    Task<AlertaClinica?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>La alerta pendiente o en atención de ese tipo de la hospitalización, o null.</summary>
    Task<AlertaClinica?> ObtenerAbiertaAsync(Guid hospitalizacionId, TipoAlerta tipo, CancellationToken ct = default);

    /// <summary>Todas las abiertas (para el vigilante), o las de un servicio.</summary>
    Task<IReadOnlyList<AlertaClinica>> ListarAbiertasAsync(Guid? servicioId, CancellationToken ct = default);

    /// <summary>Ordenadas por prioridad (alta primero) y antigüedad (la más antigua primero).</summary>
    Task<Pagina<AlertaConCama>> ListarAsync(FiltroAlertas filtro, int pagina, int tamano, CancellationToken ct = default);

    /// <summary>Todas las alertas de la hospitalización, la más reciente primero (historial del paciente).</summary>
    Task<IReadOnlyList<AlertaConCama>> ListarDeHospitalizacionAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>La última alerta atendida de ese tipo de la hospitalización, o null (espera tras atender).</summary>
    Task<AlertaClinica?> ObtenerUltimaAtendidaAsync(Guid hospitalizacionId, TipoAlerta tipo, CancellationToken ct = default);

    /// <summary>Abiertas y atendidas desde <paramref name="atendidasDesde"/> de esas hospitalizaciones (entrega de turno).</summary>
    Task<IReadOnlyList<AlertaConCama>> ListarParaEntregaAsync(IReadOnlyCollection<Guid> hospitalizaciones, DateTime atendidasDesde, CancellationToken ct = default);

    /// <summary>Cantidad y prioridad máxima de las abiertas por hospitalización.</summary>
    Task<IReadOnlyDictionary<Guid, (int Cantidad, PrioridadAlerta Maxima)>> ResumirAbiertasAsync(IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default);

    void Agregar(AlertaClinica alerta);
}

public interface IRepositorioDispositivosMoviles
{
    Task<DispositivoMovil?> ObtenerPorHashAsync(string tokenHash, CancellationToken ct = default);

    Task<IReadOnlyList<DispositivoMovil>> ListarDeUsuariosAsync(IReadOnlyCollection<Guid> usuarios, CancellationToken ct = default);

    void Agregar(DispositivoMovil dispositivo);

    void Eliminar(DispositivoMovil dispositivo);
}

/// <summary>Texto de una notificación push. Nunca lleva nombre, documento ni diagnóstico.</summary>
public sealed record MensajePush(string Titulo, string Cuerpo, IReadOnlyDictionary<string, string> Datos);

public enum ResultadoEnvioPush
{
    Enviado = 1,

    /// <summary>El token ya no existe (la app se desinstaló o renovó el token): hay que borrarlo.</summary>
    NoRegistrado = 2,

    Error = 3,
}

/// <summary>Envía notificaciones push (FCM). Nunca lanza: los fallos vuelven como <see cref="ResultadoEnvioPush.Error"/>.</summary>
public interface INotificadorPush
{
    /// <returns>Un resultado por token, en el mismo orden.</returns>
    Task<IReadOnlyList<ResultadoEnvioPush>> EnviarAsync(IReadOnlyList<string> tokens, MensajePush mensaje, CancellationToken ct = default);
}

/// <summary>Eventos en tiempo real (SignalR) a los grupos del servicio. Nunca lanza.</summary>
public interface INotificadorTiempoReal
{
    /// <param name="evento"><c>alertaCreada</c> o <c>alertaActualizada</c>.</param>
    /// <returns>Por cada destinatario, si tenía una conexión abierta.</returns>
    Task<IReadOnlyDictionary<Guid, bool>> AlertaAsync(string evento, AlertaEventoDto alerta, IReadOnlyCollection<Guid> destinatarios, CancellationToken ct = default);

    Task RiesgoActualizadoAsync(Guid servicioId, RiesgoActualizadoDto riesgo, CancellationToken ct = default);

    Task SignosActualizadosAsync(Guid servicioId, SignosActualizadosDto signos, CancellationToken ct = default);

    /// <summary>Evento <c>planActualizado</c> al grupo del servicio (fase 8).</summary>
    Task PlanActualizadoAsync(Guid servicioId, PlanActualizadoDto plan, CancellationToken ct = default);

    /// <summary>Evento <c>sugerenciasNnn</c> al grupo del servicio (fase 8).</summary>
    Task SugerenciasNnnAsync(Guid servicioId, SugerenciasNnnDto sugerencias, CancellationToken ct = default);
}

/// <summary>
/// Registro de fallos que no deben interrumpir el flujo (una notificación que no salió, un manejador que falló). La
/// capa de aplicación no tiene log: el adaptador lo escribe. Los textos llevan solo ids.
/// </summary>
public interface IBitacoraFallos
{
    void Registrar(string operacion, Exception error);
}
