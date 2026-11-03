using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Puertos;

/// <summary>Historial de lecturas (esquema <c>vitalify_historial</c>).</summary>
public interface IRepositorioLecturas
{
    Task<bool> ExisteAsync(string codigoDispositivo, DateTime medidoEn, CancellationToken ct = default);

    /// <summary>Lecturas de la hospitalización medidas entre ambos límites (inclusive), de la más antigua a la más reciente.</summary>
    Task<IReadOnlyList<LecturaSignos>> ListarPorHospitalizacionAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct = default);

    /// <summary>
    /// Lecturas de la hospitalización entre ambos límites, agrupadas cada <paramref name="segundos"/> (alineado a la
    /// época UNIX), del grupo más antiguo al más reciente. Solo aparecen los grupos con lecturas. Se agrega en la base,
    /// con las crudas y con los resúmenes de 5 min de las que ya se resumieron (<see cref="GrupoLecturas.Resumido"/>):
    /// un grupo resumido nunca es menor que 5 min, aunque se pidan grupos de 1 min.
    /// </summary>
    Task<IReadOnlyList<GrupoLecturas>> AgruparAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, int segundos, CancellationToken ct = default);

    /// <summary>Mínimo y máximo de cada variable en toda la hospitalización (crudas y resumidas).</summary>
    Task<ExtremosSignos> ObtenerExtremosAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>Fin del último grupo resumido de la hospitalización (null si no se resumió nada).</summary>
    Task<DateTime?> ObtenerResumidoHastaAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>Hospitalizaciones que tienen lecturas crudas medidas antes de <paramref name="antesDe"/>.</summary>
    Task<IReadOnlyList<Guid>> ListarHospitalizacionesConLecturasCrudasAsync(DateTime antesDe, CancellationToken ct = default);

    /// <summary>
    /// Pasa todas las lecturas crudas de la hospitalización a resúmenes de 5 min y las borra. Cada tramo (un día) se
    /// resume y se borra en una sola sentencia: nunca se borra una lectura que no quedó resumida. Si un grupo ya
    /// estaba resumido, lo combina (promedio ponderado). No necesita <see cref="IUnidadDeTrabajoHistorial"/>.
    /// </summary>
    Task<LecturasResumidas> ResumirAsync(Guid hospitalizacionId, DateTime resumidoEn, CancellationToken ct = default);

    void Agregar(LecturaSignos lectura);
}

/// <summary>Resultado de resumir las lecturas de una hospitalización.</summary>
/// <param name="Lecturas">Lecturas crudas borradas (todas quedaron en algún grupo).</param>
/// <param name="Grupos">Grupos de 5 min escritos.</param>
public sealed record LecturasResumidas(int Lecturas, int Grupos);

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
/// Pide al sensor que repita una medición (HU12 E2). El adaptador publica el comando <c>repetir-medicion</c> en
/// <c>device/{codigo}/comandos</c> (la incidencia ya queda con <c>requiere_nueva_lectura</c>).
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
