using Vitalify.Application.Alertas;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Notificaciones;
using Vitalify.Domain.Servicios;
using Vitalify.Domain.Turnos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Tests.Fakes;

public sealed class RepositorioServiciosEnMemoria : IRepositorioServicios
{
    public List<Servicio> Servicios { get; } = [];

    public Task<Servicio?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Servicios.SingleOrDefault(s => s.Id == id));

    public Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default) => Task.FromResult(Servicios.Any(s => s.Codigo == codigo));

    public Task<IReadOnlyList<Servicio>> ListarAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Servicio>>(Servicios.OrderBy(s => s.Codigo).ToList());

    public void Agregar(Servicio servicio) => Servicios.Add(servicio);
}

public sealed class RepositorioProgramacionEnMemoria : IRepositorioProgramacion
{
    public List<ProgramacionTurno> Turnos { get; } = [];

    public Task<IReadOnlyList<ProgramacionTurno>> ListarAsync(Guid servicioId, DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProgramacionTurno>>(Turnos.Where(t => t.ServicioId == servicioId && t.Fecha >= desde && t.Fecha <= hasta).ToList());

    public Task<IReadOnlyList<ProgramacionTurno>> ListarDeUsuarioAsync(Guid usuarioId, DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProgramacionTurno>>(Turnos.Where(t => t.UsuarioId == usuarioId && t.Fecha >= desde && t.Fecha <= hasta).ToList());

    public Task ReemplazarMesAsync(Guid servicioId, int anio, int mes, IReadOnlyList<ProgramacionTurno> nuevos, CancellationToken ct = default)
    {
        Turnos.RemoveAll(t => t.ServicioId == servicioId && t.Fecha.Year == anio && t.Fecha.Month == mes);
        Turnos.AddRange(nuevos);
        return Task.CompletedTask;
    }
}

public sealed class RepositorioAlertasEnMemoria(RepositorioHospitalizacionesEnMemoria hospitalizaciones) : IRepositorioAlertas
{
    public List<AlertaClinica> Alertas { get; } = [];

    public Task<AlertaClinica?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Alertas.SingleOrDefault(a => a.Id == id));

    public Task<AlertaClinica?> ObtenerAbiertaAsync(Guid hospitalizacionId, TipoAlerta tipo, CancellationToken ct = default) =>
        Task.FromResult(Alertas.SingleOrDefault(a => a.HospitalizacionId == hospitalizacionId && a.Tipo == tipo && a.EstaAbierta));

    public Task<IReadOnlyList<AlertaClinica>> ListarAbiertasAsync(Guid? servicioId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AlertaClinica>>(Alertas.Where(a => a.EstaAbierta && (servicioId is null || a.ServicioId == servicioId)).ToList());

    public Task<Pagina<AlertaConCama>> ListarAsync(FiltroAlertas filtro, int pagina, int tamano, CancellationToken ct = default)
    {
        var filas = Alertas
            .Where(a => a.ServicioId == filtro.ServicioId && filtro.Estados.Contains(a.Estado) && (filtro.Tipo is null || a.Tipo == filtro.Tipo))
            .OrderByDescending(a => a.Prioridad).ThenBy(a => a.CreadaEn)
            .Select(ConCama)
            .ToList();
        return Task.FromResult(new Pagina<AlertaConCama>(filas.Skip((pagina - 1) * tamano).Take(tamano).ToList(), pagina, tamano, filas.Count));
    }

    public Task<IReadOnlyList<AlertaConCama>> ListarDePacienteAsync(Guid pacienteId, Guid servicioId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AlertaConCama>>(Alertas.Where(a => a.PacienteId == pacienteId && a.ServicioId == servicioId).Select(ConCama).ToList());

    public Task<IReadOnlyList<AlertaConCama>> ListarParaEntregaAsync(IReadOnlyCollection<Guid> ids, DateTime atendidasDesde, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AlertaConCama>>(Alertas
            .Where(a => ids.Contains(a.HospitalizacionId) && (a.EstaAbierta || a.AtendidaEn >= atendidasDesde))
            .Select(ConCama).ToList());

    public Task<IReadOnlyDictionary<Guid, (int Cantidad, PrioridadAlerta Maxima)>> ResumirAbiertasAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, (int, PrioridadAlerta)>>(Alertas
            .Where(a => ids.Contains(a.HospitalizacionId) && a.EstaAbierta)
            .GroupBy(a => a.HospitalizacionId)
            .ToDictionary(g => g.Key, g => (g.Count(), g.Max(a => a.Prioridad))));

    public void Agregar(AlertaClinica alerta) => Alertas.Add(alerta);

    private AlertaConCama ConCama(AlertaClinica a)
    {
        var hospitalizacion = hospitalizaciones.Hospitalizaciones.Single(h => h.Id == a.HospitalizacionId);
        return new AlertaConCama(a, hospitalizaciones.Camas.Camas.Single(c => c.Id == hospitalizacion.CamaId).Codigo);
    }
}

public sealed class RepositorioMovilesEnMemoria : IRepositorioDispositivosMoviles
{
    public List<DispositivoMovil> Dispositivos { get; } = [];

    public Task<DispositivoMovil?> ObtenerPorHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Dispositivos.SingleOrDefault(d => d.TokenHash == tokenHash));

    public Task<IReadOnlyList<DispositivoMovil>> ListarDeUsuariosAsync(IReadOnlyCollection<Guid> usuarios, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DispositivoMovil>>(Dispositivos.Where(d => usuarios.Contains(d.UsuarioId)).ToList());

    public void Agregar(DispositivoMovil dispositivo) => Dispositivos.Add(dispositivo);

    public void Eliminar(DispositivoMovil dispositivo) => Dispositivos.Remove(dispositivo);
}

/// <summary>Registra los push; los tokens de <see cref="NoRegistrados"/> responden "no registrado".</summary>
public sealed class PushFalso : INotificadorPush
{
    public List<(IReadOnlyList<string> Tokens, MensajePush Mensaje)> Enviados { get; } = [];
    public HashSet<string> NoRegistrados { get; } = [];

    public Task<IReadOnlyList<ResultadoEnvioPush>> EnviarAsync(IReadOnlyList<string> tokens, MensajePush mensaje, CancellationToken ct = default)
    {
        Enviados.Add((tokens, mensaje));
        return Task.FromResult<IReadOnlyList<ResultadoEnvioPush>>(tokens
            .Select(t => NoRegistrados.Contains(t) ? ResultadoEnvioPush.NoRegistrado : ResultadoEnvioPush.Enviado).ToList());
    }
}

public sealed class TiempoRealFalso : INotificadorTiempoReal
{
    public List<(string Evento, AlertaEventoDto Alerta)> Alertas { get; } = [];
    public List<RiesgoActualizadoDto> Riesgos { get; } = [];
    public List<SignosActualizadosDto> Signos { get; } = [];
    public HashSet<Guid> Conectados { get; } = [];
    public bool Falla { get; set; }

    public Task<IReadOnlyDictionary<Guid, bool>> AlertaAsync(string evento, AlertaEventoDto alerta, IReadOnlyCollection<Guid> destinatarios, CancellationToken ct = default)
    {
        if (Falla)
        {
            throw new InvalidOperationException("SignalR no disponible.");
        }

        Alertas.Add((evento, alerta));
        return Task.FromResult<IReadOnlyDictionary<Guid, bool>>(destinatarios.Distinct().ToDictionary(d => d, Conectados.Contains));
    }

    public Task RiesgoActualizadoAsync(Guid servicioId, RiesgoActualizadoDto riesgo, CancellationToken ct = default)
    {
        Riesgos.Add(riesgo);
        return Task.CompletedTask;
    }

    public Task SignosActualizadosAsync(Guid servicioId, SignosActualizadosDto signos, CancellationToken ct = default)
    {
        Signos.Add(signos);
        return Task.CompletedTask;
    }
}

/// <summary>Como el adaptador real: programadas o, de respaldo, las enfermeras activas del servicio.</summary>
public sealed class TurnoVigenteEnMemoria(RepositorioProgramacionEnMemoria programacion, RepositorioUsuariosEnMemoria usuarios, CatalogoTurnos catalogo)
    : ITurnoVigente
{
    public Task<EnfermerasDeTurno> EnfermerasDeTurnoAsync(Guid servicioId, DateTime instante, CancellationToken ct = default)
    {
        var turnos = catalogo.TurnosEn(instante);
        var programadas = programacion.Turnos.Where(t => t.ServicioId == servicioId && turnos.Contains(t.Turno)).Select(t => t.UsuarioId).Distinct().ToList();
        if (programadas.Count > 0)
        {
            return Task.FromResult(new EnfermerasDeTurno(programadas, false));
        }

        var activas = usuarios.Usuarios.Where(u => u.Rol == Rol.Enfermera && u.Activo && u.ServicioId == servicioId).Select(u => u.Id).ToList();
        return Task.FromResult(new EnfermerasDeTurno(activas, true));
    }
}

public sealed class BitacoraFalsa : IBitacoraFallos
{
    public List<(string Operacion, Exception Error)> Fallos { get; } = [];

    public void Registrar(string operacion, Exception error) => Fallos.Add((operacion, error));
}
