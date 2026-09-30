using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;
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
public sealed class HistorialEnMemoria : IRepositorioLecturas, IRepositorioEstadoSignos, IRepositorioIncidencias, IUnidadDeTrabajoHistorial
{
    private readonly List<LecturaSignos> _lecturasPendientes = [];
    private readonly List<EstadoSignosActual> _estadosPendientes = [];
    private readonly List<IncidenciaTelemetria> _incidenciasPendientes = [];

    public List<LecturaSignos> Lecturas { get; } = [];
    public List<EstadoSignosActual> Estados { get; } = [];
    public List<IncidenciaTelemetria> Incidencias { get; } = [];
    public int Guardados { get; private set; }

    /// <summary>Errores que devolverán los próximos guardados, en orden (simula carreras en la base).</summary>
    public Queue<Error> ErroresAlGuardar { get; } = new();

    public Task<bool> ExisteAsync(string codigoDispositivo, DateTime medidoEn, CancellationToken ct = default) =>
        Task.FromResult(Lecturas.Any(l => l.CodigoDispositivo == codigoDispositivo && l.MedidoEn == medidoEn));

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

    public Task<Resultado<Unidad>> GuardarCambiosAsync(CancellationToken ct = default)
    {
        Guardados++;
        if (ErroresAlGuardar.TryDequeue(out var error))
        {
            _lecturasPendientes.Clear();
            _estadosPendientes.Clear();
            _incidenciasPendientes.Clear();
            return Task.FromResult<Resultado<Unidad>>(error);
        }

        Lecturas.AddRange(_lecturasPendientes);
        Estados.AddRange(_estadosPendientes);
        Incidencias.AddRange(_incidenciasPendientes);
        _lecturasPendientes.Clear();
        _estadosPendientes.Clear();
        _incidenciasPendientes.Clear();
        return Task.FromResult<Resultado<Unidad>>(Unidad.Valor);
    }
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
