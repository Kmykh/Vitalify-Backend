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

    public void Agregar(EvaluacionRiesgo evaluacion) => _evaluacionesPendientes.Add(evaluacion);

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
