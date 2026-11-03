using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Tests.Fakes;

/// <summary>Resuelve contra los fakes de la fase 2 y registra las invalidaciones de caché.</summary>
public sealed class ResolutorDispositivosFalso(RepositorioHospitalizacionesEnMemoria hospitalizaciones, RepositorioDispositivosEnMemoria dispositivos)
    : IResolutorDispositivos
{
    public List<string> Invalidaciones { get; } = [];

    public async Task<ResolucionDispositivo> ResolverAsync(string codigoDispositivo, CancellationToken ct = default)
    {
        if (await hospitalizaciones.ObtenerActivaPorCodigoDispositivoAsync(codigoDispositivo, ct) is { } hospitalizacion)
        {
            return new ResolucionDispositivo(EstadoDispositivoEnIngesta.Vinculado, hospitalizacion);
        }

        return new ResolucionDispositivo(
            await dispositivos.ExisteCodigoAsync(codigoDispositivo, ct) ? EstadoDispositivoEnIngesta.SinVincular : EstadoDispositivoEnIngesta.Desconocido,
            null);
    }

    public void Invalidar(string codigoDispositivo) => Invalidaciones.Add(codigoDispositivo);
}

/// <summary>Historial en memoria: lo agregado queda pendiente hasta que la unidad de trabajo lo confirma.</summary>
public sealed class HistorialEnMemoria
    : IRepositorioLecturas, IRepositorioEstadoSignos, IRepositorioIncidencias, IRepositorioEvaluaciones, IRepositorioObservaciones, IUnidadDeTrabajoHistorial
{
    private readonly List<LecturaSignos> _lecturasPendientes = [];
    private readonly List<EstadoSignosActual> _estadosPendientes = [];
    private readonly List<IncidenciaTelemetria> _incidenciasPendientes = [];
    private readonly List<EvaluacionRiesgo> _evaluacionesPendientes = [];
    private readonly List<ObservacionEnfermeria> _observacionesPendientes = [];

    public List<LecturaSignos> Lecturas { get; } = [];
    public List<EstadoSignosActual> Estados { get; } = [];
    public List<IncidenciaTelemetria> Incidencias { get; } = [];
    public List<EvaluacionRiesgo> Evaluaciones { get; } = [];
    public List<ObservacionEnfermeria> Observaciones { get; } = [];
    public int Guardados { get; private set; }

    /// <summary>Errores que devolverán los próximos guardados, en orden (simula carreras en la base).</summary>
    public Queue<Error> ErroresAlGuardar { get; } = new();

    public Task<bool> ExisteAsync(string codigoDispositivo, DateTime medidoEn, CancellationToken ct = default) =>
        Task.FromResult(Lecturas.Any(l => l.CodigoDispositivo == codigoDispositivo && l.MedidoEn == medidoEn));

    public Task<IReadOnlyList<LecturaSignos>> ListarPorHospitalizacionAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LecturaSignos>>(Lecturas
            .Where(l => l.HospitalizacionId == hospitalizacionId && l.MedidoEn >= desde && l.MedidoEn <= hasta)
            .OrderBy(l => l.MedidoEn)
            .ToList());

    public void Agregar(LecturaSignos lectura) => _lecturasPendientes.Add(lectura);

    public Task<EstadoSignosActual?> ObtenerAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        Task.FromResult(Estados.Concat(_estadosPendientes).SingleOrDefault(e => e.HospitalizacionId == hospitalizacionId));

    public Task<IReadOnlyDictionary<Guid, EstadoSignosActual>> ObtenerVariosAsync(IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, EstadoSignosActual>>(Estados.Where(e => hospitalizaciones.Contains(e.HospitalizacionId)).ToDictionary(e => e.HospitalizacionId));

    public void Agregar(EstadoSignosActual estado) => _estadosPendientes.Add(estado);

    public void Agregar(IncidenciaTelemetria incidencia) => _incidenciasPendientes.Add(incidencia);

    public Task<Pagina<IncidenciaTelemetria>> ListarAsync(FiltroIncidencias filtro, int pagina, int tamano, CancellationToken ct = default)
    {
        var filtradas = Incidencias
            .Where(i => filtro.Tipo is null || i.Tipo == filtro.Tipo)
            .Where(i => filtro.CodigoDispositivo is null || i.CodigoDispositivo == filtro.CodigoDispositivo)
            .Where(i => filtro.Desde is null || i.OcurridaEn >= filtro.Desde)
            .Where(i => filtro.Hasta is null || i.OcurridaEn <= filtro.Hasta)
            .OrderByDescending(i => i.OcurridaEn)
            .ToList();
        return Task.FromResult(new Pagina<IncidenciaTelemetria>(filtradas.Skip((pagina - 1) * tamano).Take(tamano).ToList(), pagina, tamano, filtradas.Count));
    }

    public Task<IReadOnlyList<GrupoLecturas>> AgruparAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, int segundos, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GrupoLecturas>>(Lecturas
            .Where(l => l.HospitalizacionId == hospitalizacionId && l.MedidoEn >= desde && l.MedidoEn <= hasta)
            .GroupBy(l => Grupo(l.MedidoEn, segundos))
            .OrderBy(g => g.Key)
            .Select(g => new GrupoLecturas(g.Key, g.Count(),
                Estadistica(g.Select(l => (decimal?)l.Fc)), Estadistica(g.Select(l => (decimal?)l.Fr)), Estadistica(g.Select(l => (decimal?)l.Spo2)),
                Estadistica(g.Select(l => l.Temperatura)), Estadistica(g.Select(l => (decimal?)l.Pas)), Estadistica(g.Select(l => (decimal?)l.Pad)),
                g.Any(l => l.Caida)))
            .ToList());

    public Task<ExtremosSignos> ObtenerExtremosAsync(Guid hospitalizacionId, CancellationToken ct = default)
    {
        var de = Lecturas.Where(l => l.HospitalizacionId == hospitalizacionId).ToList();
        return Task.FromResult(new ExtremosSignos(
            Rango(de.Select(l => (decimal?)l.Fc)), Rango(de.Select(l => (decimal?)l.Fr)), Rango(de.Select(l => (decimal?)l.Spo2)),
            Rango(de.Select(l => l.Temperatura)), Rango(de.Select(l => (decimal?)l.Pas)), Rango(de.Select(l => (decimal?)l.Pad))));
    }

    /// <summary>Como el SQL: <c>floor(epoch / segundos) * segundos</c>.</summary>
    private static DateTime Grupo(DateTime instante, int segundos) =>
        DateTime.UnixEpoch.AddSeconds(Math.Floor((instante - DateTime.UnixEpoch).TotalSeconds / segundos) * segundos);

    private static EstadisticaSerie? Estadistica(IEnumerable<decimal?> valores)
    {
        var medidos = valores.Where(v => v is not null).Select(v => v!.Value).ToList();
        return medidos.Count == 0 ? null : new EstadisticaSerie(Math.Round(medidos.Average(), 1, MidpointRounding.AwayFromZero), medidos.Min(), medidos.Max());
    }

    private static RangoValor? Rango(IEnumerable<decimal?> valores)
    {
        var medidos = valores.Where(v => v is not null).Select(v => v!.Value).ToList();
        return medidos.Count == 0 ? null : new RangoValor(medidos.Min(), medidos.Max(), medidos.Count);
    }

    public void Agregar(EvaluacionRiesgo evaluacion) => _evaluacionesPendientes.Add(evaluacion);

    public Task<IReadOnlyList<EvaluacionRiesgo>> ListarAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EvaluacionRiesgo>>(Evaluaciones
            .Where(e => e.HospitalizacionId == hospitalizacionId && e.EvaluadaEn >= desde && e.EvaluadaEn <= hasta)
            .OrderBy(e => e.EvaluadaEn).ToList());

    Task<IReadOnlyList<GrupoEvaluaciones>> IRepositorioEvaluaciones.AgruparAsync(
        Guid hospitalizacionId, DateTime desde, DateTime hasta, int segundos, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GrupoEvaluaciones>>(Evaluaciones
            .Where(e => e.HospitalizacionId == hospitalizacionId && e.EvaluadaEn >= desde && e.EvaluadaEn <= hasta)
            .GroupBy(e => Grupo(e.EvaluadaEn, segundos))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var orden = g.OrderBy(e => e.EvaluadaEn).ToList();
                return new GrupoEvaluaciones(g.Key, orden.Count, orden.Count(e => e.News2Completo && e.MewsCompleto),
                    orden.Min(e => e.News2Total), orden.Max(e => e.News2Total), orden[^1].News2Total, orden.Max(e => e.News2Nivel),
                    orden.Min(e => e.MewsTotal), orden.Max(e => e.MewsTotal), orden[^1].MewsTotal, orden.Max(e => e.MewsNivel));
            })
            .ToList());

    Task<IReadOnlyList<ObservacionEnfermeria>> IRepositorioObservaciones.ListarAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ObservacionEnfermeria>>(Observaciones
            .Where(o => o.HospitalizacionId == hospitalizacionId && o.ObservadaEn >= desde && o.ObservadaEn <= hasta)
            .OrderByDescending(o => o.ObservadaEn).ToList());

    public Task<EvaluacionRiesgo?> ObtenerUltimaAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        Task.FromResult(Evaluaciones.Where(e => e.HospitalizacionId == hospitalizacionId).OrderByDescending(e => e.EvaluadaEn).FirstOrDefault());

    public Task<IReadOnlyDictionary<Guid, EvaluacionRiesgo>> ObtenerUltimasAsync(IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, EvaluacionRiesgo>>(Evaluaciones
            .Where(e => hospitalizaciones.Contains(e.HospitalizacionId))
            .GroupBy(e => e.HospitalizacionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.EvaluadaEn).First()));

    public void Agregar(ObservacionEnfermeria observacion) => _observacionesPendientes.Add(observacion);

    public Task<IReadOnlyList<ObservacionEnfermeria>> ListarDesdeAsync(Guid hospitalizacionId, DateTime desde, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ObservacionEnfermeria>>(Observaciones.Concat(_observacionesPendientes)
            .Where(o => o.HospitalizacionId == hospitalizacionId && o.ObservadaEn >= desde)
            .OrderByDescending(o => o.ObservadaEn)
            .ToList());

    public Task<ObservacionEnfermeria?> ObtenerUltimaTomaAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        Task.FromResult(Observaciones.Where(o => o.HospitalizacionId == hospitalizacionId).OrderByDescending(o => o.ObservadaEn).FirstOrDefault());

    public Task<BrazoPresion?> ObtenerUltimoBrazoPresionAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        Task.FromResult(Observaciones.Where(o => o.HospitalizacionId == hospitalizacionId && o.BrazoPa is not null)
            .OrderByDescending(o => o.ObservadaEn).Select(o => o.BrazoPa).FirstOrDefault());

    public Task<IReadOnlyDictionary<Guid, ObservacionEnfermeria>> ObtenerUltimasPreocupacionesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ObservacionEnfermeria>>(Observaciones
            .Where(o => ids.Contains(o.HospitalizacionId) && o.PreocupacionClinica == true)
            .GroupBy(o => o.HospitalizacionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(o => o.ObservadaEn).First()));

    public Task<IReadOnlyDictionary<Guid, TendenciaRiesgo>> ObtenerTendenciasAsync(IReadOnlyCollection<Guid> ids, DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, TendenciaRiesgo>>(Evaluaciones
            .Where(e => ids.Contains(e.HospitalizacionId) && e.EvaluadaEn >= desde && e.EvaluadaEn <= hasta)
            .GroupBy(e => e.HospitalizacionId)
            .ToDictionary(g => g.Key, g =>
            {
                var orden = g.OrderBy(e => e.EvaluadaEn).ToList();
                return new TendenciaRiesgo(
                    new TendenciaPuntaje(orden[0].News2Total, orden.Max(e => e.News2Total), orden[^1].News2Total),
                    new TendenciaPuntaje(orden[0].MewsTotal, orden.Max(e => e.MewsTotal), orden[^1].MewsTotal),
                    orden.Count);
            }));

    public Task<Resultado<Unidad>> GuardarCambiosAsync(CancellationToken ct = default)
    {
        Guardados++;
        if (ErroresAlGuardar.TryDequeue(out var error))
        {
            Descartar();
            return Task.FromResult<Resultado<Unidad>>(error);
        }

        Lecturas.AddRange(_lecturasPendientes);
        Estados.AddRange(_estadosPendientes);
        Incidencias.AddRange(_incidenciasPendientes);
        Evaluaciones.AddRange(_evaluacionesPendientes);
        Observaciones.AddRange(_observacionesPendientes);
        Descartar();
        return Task.FromResult<Resultado<Unidad>>(Unidad.Valor);
    }

    private void Descartar()
    {
        _lecturasPendientes.Clear();
        _estadosPendientes.Clear();
        _incidenciasPendientes.Clear();
        _evaluacionesPendientes.Clear();
        _observacionesPendientes.Clear();
    }
}

public sealed class PresenciaEnMemoria : IPresenciaDispositivos
{
    private readonly Dictionary<string, PresenciaDispositivo> _presencias = new();

    public void Registrar(string codigoDispositivo, PresenciaDispositivo presencia) => _presencias[codigoDispositivo] = presencia;

    public PresenciaDispositivo? Obtener(string codigoDispositivo) => _presencias.GetValueOrDefault(codigoDispositivo);

    public IReadOnlyList<string> ListarEnLinea() => _presencias.Where(p => p.Value.EnLinea).Select(p => p.Key).ToList();
}

public sealed class SolicitudNuevaLecturaFalsa : ISolicitudNuevaLectura
{
    public List<SolicitudNuevaLectura> Solicitudes { get; } = [];

    public Task SolicitarAsync(SolicitudNuevaLectura solicitud, CancellationToken ct = default)
    {
        Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }
}

public sealed class ManejadorLecturaRegistradaFalso : IManejadorLecturaRegistrada
{
    public List<LecturaRegistrada> Eventos { get; } = [];

    public Task ManejarAsync(LecturaRegistrada lectura, CancellationToken ct = default)
    {
        Eventos.Add(lectura);
        return Task.CompletedTask;
    }
}
