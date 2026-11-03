using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Notificaciones;
using Vitalify.Domain.Servicios;
using Vitalify.Domain.Turnos;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioServicios(TransaccionalDbContext db) : IRepositorioServicios
{
    public async Task<Servicio?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) => await db.Servicios.FindAsync([id], ct);

    public Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default) => db.Servicios.AnyAsync(s => s.Codigo == codigo, ct);

    public async Task<IReadOnlyList<Servicio>> ListarAsync(CancellationToken ct = default) =>
        await db.Servicios.AsNoTracking().OrderBy(s => s.Codigo).ToListAsync(ct);

    public void Agregar(Servicio servicio) => db.Servicios.Add(servicio);
}

internal sealed class RepositorioProgramacion(TransaccionalDbContext db) : IRepositorioProgramacion
{
    public async Task<IReadOnlyList<ProgramacionTurno>> ListarAsync(Guid servicioId, DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        await db.ProgramacionTurnos.AsNoTracking()
            .Where(p => p.ServicioId == servicioId && p.Fecha >= desde && p.Fecha <= hasta)
            .OrderBy(p => p.Fecha).ThenBy(p => p.TipoTurno).ThenBy(p => p.Letra)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProgramacionTurno>> ListarDeUsuarioAsync(Guid usuarioId, DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        await db.ProgramacionTurnos.AsNoTracking()
            .Where(p => p.UsuarioId == usuarioId && p.Fecha >= desde && p.Fecha <= hasta)
            .ToListAsync(ct);

    public async Task ReemplazarMesAsync(Guid servicioId, int anio, int mes, IReadOnlyList<ProgramacionTurno> nuevos, CancellationToken ct = default)
    {
        var primero = new DateOnly(anio, mes, 1);
        var ultimo = primero.AddMonths(1).AddDays(-1);
        var anteriores = await db.ProgramacionTurnos.Where(p => p.ServicioId == servicioId && p.Fecha >= primero && p.Fecha <= ultimo).ToListAsync(ct);
        db.ProgramacionTurnos.RemoveRange(anteriores);
        db.ProgramacionTurnos.AddRange(nuevos);
    }
}

internal sealed class RepositorioAlertas(TransaccionalDbContext db) : IRepositorioAlertas
{
    private static readonly EstadoAlerta[] Abiertas = [EstadoAlerta.Pendiente, EstadoAlerta.EnAtencion];

    public async Task<AlertaClinica?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) => await db.Alertas.FindAsync([id], ct);

    public Task<AlertaClinica?> ObtenerAbiertaAsync(Guid hospitalizacionId, TipoAlerta tipo, CancellationToken ct = default) =>
        db.Alertas.SingleOrDefaultAsync(a => a.HospitalizacionId == hospitalizacionId && a.Tipo == tipo && Abiertas.Contains(a.Estado), ct);

    public async Task<IReadOnlyList<AlertaClinica>> ListarAbiertasAsync(Guid? servicioId, CancellationToken ct = default) =>
        await db.Alertas
            .Where(a => Abiertas.Contains(a.Estado) && (servicioId == null || a.ServicioId == servicioId))
            .OrderByDescending(a => a.Prioridad).ThenBy(a => a.CreadaEn)
            .ToListAsync(ct);

    public async Task<Pagina<AlertaConCama>> ListarAsync(FiltroAlertas filtro, int pagina, int tamano, CancellationToken ct = default)
    {
        var alertas = ConCama().Where(x => x.Alerta.ServicioId == filtro.ServicioId && filtro.Estados.Contains(x.Alerta.Estado));
        if (filtro.Tipo is { } tipo)
        {
            alertas = alertas.Where(x => x.Alerta.Tipo == tipo);
        }

        if (filtro.Desde is { } desde)
        {
            alertas = alertas.Where(x => x.Alerta.CreadaEn >= desde);
        }

        if (filtro.Hasta is { } hasta)
        {
            alertas = alertas.Where(x => x.Alerta.CreadaEn <= hasta);
        }

        var total = await alertas.CountAsync(ct);
        var filas = await alertas
            .OrderByDescending(x => x.Alerta.Prioridad).ThenBy(x => x.Alerta.CreadaEn).ThenBy(x => x.Alerta.Id)
            .Skip((pagina - 1) * tamano).Take(tamano)
            .ToListAsync(ct);
        return new Pagina<AlertaConCama>(filas.Select(x => new AlertaConCama(x.Alerta, x.Cama)).ToList(), pagina, tamano, total);
    }

    public async Task<IReadOnlyList<AlertaConCama>> ListarDeHospitalizacionAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        (await ConCama().Where(x => x.Alerta.HospitalizacionId == hospitalizacionId)
            .OrderByDescending(x => x.Alerta.CreadaEn).ThenBy(x => x.Alerta.Id)
            .ToListAsync(ct))
        .Select(x => new AlertaConCama(x.Alerta, x.Cama))
        .ToList();

    public Task<AlertaClinica?> ObtenerUltimaAtendidaAsync(Guid hospitalizacionId, TipoAlerta tipo, CancellationToken ct = default) =>
        db.Alertas.AsNoTracking()
            .Where(a => a.HospitalizacionId == hospitalizacionId && a.Tipo == tipo && a.Estado == EstadoAlerta.Atendida)
            .OrderByDescending(a => a.AtendidaEn)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<AlertaConCama>> ListarParaEntregaAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, DateTime atendidasDesde, CancellationToken ct = default) =>
        hospitalizaciones.Count == 0
            ? []
            : (await ConCama()
                .Where(x => hospitalizaciones.Contains(x.Alerta.HospitalizacionId)
                            && (Abiertas.Contains(x.Alerta.Estado) || x.Alerta.AtendidaEn >= atendidasDesde))
                .ToListAsync(ct))
            .Select(x => new AlertaConCama(x.Alerta, x.Cama))
            .ToList();

    public async Task<IReadOnlyDictionary<Guid, (int Cantidad, PrioridadAlerta Maxima)>> ResumirAbiertasAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default)
    {
        if (hospitalizaciones.Count == 0)
        {
            return new Dictionary<Guid, (int, PrioridadAlerta)>();
        }

        var abiertas = await db.Alertas.AsNoTracking()
            .Where(a => hospitalizaciones.Contains(a.HospitalizacionId) && Abiertas.Contains(a.Estado))
            .Select(a => new { a.HospitalizacionId, a.Prioridad })
            .ToListAsync(ct);
        return abiertas
            .GroupBy(a => a.HospitalizacionId)
            .ToDictionary(g => g.Key, g => (g.Count(), g.Max(a => a.Prioridad)));
    }

    public void Agregar(AlertaClinica alerta) => db.Alertas.Add(alerta);

    private IQueryable<FilaAlerta> ConCama() =>
        from a in db.Alertas.AsNoTracking()
        join h in db.Hospitalizaciones on a.HospitalizacionId equals h.Id
        join c in db.Camas on h.CamaId equals c.Id
        select new FilaAlerta { Alerta = a, Cama = c.Codigo };

    private sealed class FilaAlerta
    {
        public AlertaClinica Alerta { get; init; } = null!;
        public string Cama { get; init; } = null!;
    }
}

internal sealed class RepositorioDispositivosMoviles(TransaccionalDbContext db) : IRepositorioDispositivosMoviles
{
    public Task<DispositivoMovil?> ObtenerPorHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.DispositivosMoviles.SingleOrDefaultAsync(d => d.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<DispositivoMovil>> ListarDeUsuariosAsync(IReadOnlyCollection<Guid> usuarios, CancellationToken ct = default) =>
        usuarios.Count == 0 ? [] : await db.DispositivosMoviles.Where(d => usuarios.Contains(d.UsuarioId)).ToListAsync(ct);

    public void Agregar(DispositivoMovil dispositivo) => db.DispositivosMoviles.Add(dispositivo);

    public void Eliminar(DispositivoMovil dispositivo) => db.DispositivosMoviles.Remove(dispositivo);
}
