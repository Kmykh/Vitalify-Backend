using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Puertos;

/// <summary>Historial de lecturas (esquema <c>vitalify_historial</c>; en la fase 7, TimescaleDB).</summary>
public interface IRepositorioLecturas
{
    Task<bool> ExisteAsync(string codigoDispositivo, DateTime medidoEn, CancellationToken ct = default);

    /// <summary>Lecturas de la hospitalización medidas entre ambos límites (inclusive), de la más antigua a la más reciente.</summary>
    Task<IReadOnlyList<LecturaSignos>> ListarPorHospitalizacionAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct = default);

    void Agregar(LecturaSignos lectura);
}

public interface IRepositorioEstadoSignos
{
    Task<EstadoSignosActual?> ObtenerAsync(Guid hospitalizacionId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, EstadoSignosActual>> ObtenerVariosAsync(IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default);

    void Agregar(EstadoSignosActual estado);
}

public sealed record FiltroIncidencias(DateTime? Desde, DateTime? Hasta, string? CodigoDispositivo, TipoIncidencia? Tipo);

public interface IRepositorioIncidencias
{
    void Agregar(IncidenciaTelemetria incidencia);

    Task<Pagina<IncidenciaTelemetria>> ListarAsync(FiltroIncidencias filtro, int pagina, int tamano, CancellationToken ct = default);
}

/// <summary>Unidad de trabajo del historial (<c>HistorialDbContext</c>), separada de la transaccional.</summary>
public interface IUnidadDeTrabajoHistorial
{
    /// <summary>
    /// Guarda lecturas, estado e incidencias en una transacción. Una lectura repetida (misma clave dispositivo +
    /// marca de tiempo) devuelve el conflicto <c>lectura-duplicada</c>; una escritura concurrente del estado,
    /// <c>conflicto-concurrencia</c>.
    /// </summary>
    Task<Resultado<Unidad>> GuardarCambiosAsync(CancellationToken ct = default);
}

public sealed record SolicitudNuevaLectura(string CodigoDispositivo, DateTime MedidoEn, string Motivo);

/// <summary>
/// Pide al sensor que repita una medición (HU12 E2). Hoy el adaptador solo lo escribe en el log (la incidencia ya
/// queda con <c>requiere_nueva_lectura</c>); en la fase 7 publicará un comando MQTT al dispositivo.
/// </summary>
public interface ISolicitudNuevaLectura
{
    Task SolicitarAsync(SolicitudNuevaLectura solicitud, CancellationToken ct = default);
}

public enum EstadoDispositivoEnIngesta
{
    Vinculado = 1,
    SinVincular = 2,
    Desconocido = 3,
}

public sealed record ResolucionDispositivo(EstadoDispositivoEnIngesta Estado, HospitalizacionPorDispositivoDto? Hospitalizacion);

/// <summary>
/// ¿A qué hospitalización activa pertenece un dispositivo? Con caché (15 s por defecto) para no consultar la base en
/// cada lectura. Vincular, liberar y egresar la invalidan en esta instancia; con varias instancias (fase 7), un
/// cambio tarda como máximo la duración de la caché en verse en las demás.
/// </summary>
public interface IResolutorDispositivos
{
    Task<ResolucionDispositivo> ResolverAsync(string codigoDispositivo, CancellationToken ct = default);

    void Invalidar(string codigoDispositivo);
}
