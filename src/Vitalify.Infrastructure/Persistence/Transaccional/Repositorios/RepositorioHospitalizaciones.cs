using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Pacientes;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioHospitalizaciones(TransaccionalDbContext db) : IRepositorioHospitalizaciones
{
    public Task<Hospitalizacion?> ObtenerActivaDePacienteAsync(Guid pacienteId, CancellationToken ct = default) =>
        db.Hospitalizaciones.SingleOrDefaultAsync(h => h.PacienteId == pacienteId && h.Estado == EstadoHospitalizacion.Activa, ct);

    public Task<bool> CamaOcupadaAsync(Guid camaId, CancellationToken ct = default) =>
        db.Hospitalizaciones.AnyAsync(h => h.CamaId == camaId && h.Estado == EstadoHospitalizacion.Activa, ct);

    public Task<AsignacionDispositivo?> ObtenerAsignacionVigenteAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        db.AsignacionesDispositivo.SingleOrDefaultAsync(a => a.HospitalizacionId == hospitalizacionId && a.LiberadoEn == null, ct);

    public Task<string?> ObtenerCamaConDispositivoAsync(Guid dispositivoId, CancellationToken ct = default) =>
        (from a in db.AsignacionesDispositivo
         where a.DispositivoId == dispositivoId && a.LiberadoEn == null
         join h in db.Hospitalizaciones on a.HospitalizacionId equals h.Id
         join c in db.Camas on h.CamaId equals c.Id
         select c.Codigo).FirstOrDefaultAsync(ct);

    public async Task<HospitalizacionActivaLectura?> ObtenerLecturaActivaAsync(Guid pacienteId, CancellationToken ct = default) =>
        (await Filas().Where(f => f.PacienteId == pacienteId).FirstOrDefaultAsync(ct))?.ALectura();

    public async Task<Pagina<HospitalizacionActivaLectura>> ListarActivasAsync(int pagina, int tamano, string? buscar, CancellationToken ct = default)
    {
        var filas = Filas();
        if (!string.IsNullOrWhiteSpace(buscar))
        {
            var patron = $"%{EscaparLike(buscar)}%";
            var documento = Paciente.NormalizarDocumento(buscar);
            filas = filas.Where(f =>
                EF.Functions.ILike(f.NombreCompleto, patron, @"\")
                || EF.Functions.ILike(f.CodigoCama, patron, @"\")
                || f.NumeroDocumento == documento);
        }

        var total = await filas.CountAsync(ct);
        var elementos = await filas
            .OrderBy(f => f.CodigoCama).ThenBy(f => f.HospitalizacionId)
            .Skip((pagina - 1) * tamano).Take(tamano)
            .ToListAsync(ct);

        return new Pagina<HospitalizacionActivaLectura>(elementos.Select(f => f.ALectura()).ToList(), pagina, tamano, total);
    }

    public async Task<IReadOnlyList<HospitalizacionActivaLectura>> ListarActivasConDispositivoAsync(CancellationToken ct = default) =>
        (await Filas().Where(f => f.DispositivoId != null).OrderBy(f => f.CodigoCama).ToListAsync(ct))
            .Select(f => f.ALectura())
            .ToList();

    public Task<HospitalizacionPorDispositivoDto?> ObtenerActivaPorCodigoDispositivoAsync(string codigoDispositivo, CancellationToken ct = default) =>
        (from d in db.Dispositivos
         where d.Codigo == codigoDispositivo
         join a in db.AsignacionesDispositivo on d.Id equals a.DispositivoId
         where a.LiberadoEn == null
         join h in db.Hospitalizaciones on a.HospitalizacionId equals h.Id
         where h.Estado == EstadoHospitalizacion.Activa
         select new HospitalizacionPorDispositivoDto(h.Id, h.PacienteId, h.CamaId, d.Id, a.Id, a.AsignadoEn))
        .AsNoTracking()
        .SingleOrDefaultAsync(ct);

    public void Agregar(Hospitalizacion hospitalizacion) => db.Hospitalizaciones.Add(hospitalizacion);

    public void AgregarAsignacion(AsignacionDispositivo asignacion) => db.AsignacionesDispositivo.Add(asignacion);

    /// <summary>Hospitalizaciones activas con paciente, cama y, si hay, el sensor vigente (left join).</summary>
    private IQueryable<FilaActiva> Filas() =>
        from h in db.Hospitalizaciones.AsNoTracking()
        where h.Estado == EstadoHospitalizacion.Activa
        join p in db.Pacientes on h.PacienteId equals p.Id
        join c in db.Camas on h.CamaId equals c.Id
        from a in db.AsignacionesDispositivo.Where(a => a.HospitalizacionId == h.Id && a.LiberadoEn == null).DefaultIfEmpty()
        from d in db.Dispositivos.Where(d => d.Id == a.DispositivoId).DefaultIfEmpty()
        select new FilaActiva
        {
            PacienteId = p.Id,
            NombreCompleto = p.NombreCompleto,
            TipoDocumento = p.TipoDocumento,
            NumeroDocumento = p.NumeroDocumento,
            FechaNacimiento = p.FechaNacimiento,
            FechaNacimientoEstimada = p.FechaNacimientoEstimada,
            HospitalizacionId = h.Id,
            DiagnosticoIngreso = h.DiagnosticoIngreso,
            IngresoEn = h.IngresoEn,
            CamaId = c.Id,
            CodigoCama = c.Codigo,
            Servicio = c.Servicio,
            DispositivoId = d == null ? null : d.Id,
            CodigoDispositivo = d == null ? null : d.Codigo,
            DispositivoAsignadoEn = a == null ? null : a.AsignadoEn,
        };

    private static string EscaparLike(string texto) =>
        texto.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>Proyección intermedia con propiedades asignables, para poder filtrar y ordenar en SQL.</summary>
    private sealed class FilaActiva
    {
        public Guid PacienteId { get; init; }
        public string NombreCompleto { get; init; } = null!;
        public TipoDocumento TipoDocumento { get; init; }
        public string NumeroDocumento { get; init; } = null!;
        public DateOnly FechaNacimiento { get; init; }
        public bool FechaNacimientoEstimada { get; init; }
        public Guid HospitalizacionId { get; init; }
        public string DiagnosticoIngreso { get; init; } = null!;
        public DateTime IngresoEn { get; init; }
        public Guid CamaId { get; init; }
        public string CodigoCama { get; init; } = null!;
        public string Servicio { get; init; } = null!;
        public Guid? DispositivoId { get; init; }
        public string? CodigoDispositivo { get; init; }
        public DateTime? DispositivoAsignadoEn { get; init; }

        public HospitalizacionActivaLectura ALectura() => new(
            PacienteId, NombreCompleto, TipoDocumento, NumeroDocumento, FechaNacimiento, FechaNacimientoEstimada,
            HospitalizacionId, DiagnosticoIngreso, IngresoEn, CamaId, CodigoCama, Servicio,
            DispositivoId, CodigoDispositivo, DispositivoAsignadoEn);
    }
}
