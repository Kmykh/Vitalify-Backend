using Vitalify.Application.Camas;
using Vitalify.Application.Comun;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Camas;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Pacientes;

namespace Vitalify.Application.Tests.Fakes;

public sealed class RepositorioCamasEnMemoria(Func<Guid, bool> ocupada) : IRepositorioCamas
{
    public List<Cama> Camas { get; } = [];

    public Task<Cama?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Camas.SingleOrDefault(c => c.Id == id));

    public Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default) => Task.FromResult(Camas.Any(c => c.Codigo == codigo));

    public Task<IReadOnlyList<CamaDto>> ListarAsync(bool soloDisponibles, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CamaDto>>(Camas
            .Select(c => new CamaDto(c.Id, c.Codigo, c.Servicio, c.Activa, ocupada(c.Id)))
            .Where(c => !soloDisponibles || (c.Activa && !c.Ocupada))
            .OrderBy(c => c.Codigo)
            .ToList());

    public void Agregar(Cama cama) => Camas.Add(cama);
}

public sealed class RepositorioDispositivosEnMemoria(Func<Guid, string?> camaDe) : IRepositorioDispositivos
{
    public List<Dispositivo> Dispositivos { get; } = [];

    public Task<Dispositivo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Dispositivos.SingleOrDefault(d => d.Id == id));

    public Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default) =>
        Task.FromResult(Dispositivos.Any(d => d.Codigo == codigo));

    public Task<IReadOnlyList<DispositivoDto>> ListarAsync(EstadoDispositivo? estado, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DispositivoDto>>(Dispositivos
            .Where(d => estado is null || d.Estado == estado)
            .OrderBy(d => d.Codigo)
            .Select(d => DispositivoDto.Desde(d, camaDe(d.Id)))
            .ToList());

    public void Agregar(Dispositivo dispositivo) => Dispositivos.Add(dispositivo);
}

public sealed class RepositorioPacientesEnMemoria : IRepositorioPacientes
{
    public List<Paciente> Pacientes { get; } = [];

    public Task<Paciente?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Pacientes.SingleOrDefault(p => p.Id == id));

    public Task<Paciente?> ObtenerPorDocumentoAsync(TipoDocumento tipo, string numero, CancellationToken ct = default) =>
        Task.FromResult(Pacientes.SingleOrDefault(p => p.TipoDocumento == tipo && p.NumeroDocumento == numero));

    public void Agregar(Paciente paciente) => Pacientes.Add(paciente);
}

/// <summary>Arma los modelos de lectura cruzando los demás repositorios en memoria, como haría la consulta SQL.</summary>
public sealed class RepositorioHospitalizacionesEnMemoria : IRepositorioHospitalizaciones
{
    public List<Hospitalizacion> Hospitalizaciones { get; } = [];
    public List<AsignacionDispositivo> Asignaciones { get; } = [];

    public RepositorioPacientesEnMemoria Pacientes { get; set; } = null!;
    public RepositorioCamasEnMemoria Camas { get; set; } = null!;
    public RepositorioDispositivosEnMemoria Dispositivos { get; set; } = null!;

    public Task<Hospitalizacion?> ObtenerActivaDePacienteAsync(Guid pacienteId, CancellationToken ct = default) =>
        Task.FromResult(Hospitalizaciones.SingleOrDefault(h => h.PacienteId == pacienteId && h.EstaActiva));

    public Task<bool> CamaOcupadaAsync(Guid camaId, CancellationToken ct = default) => Task.FromResult(Ocupada(camaId));

    public bool Ocupada(Guid camaId) => Hospitalizaciones.Any(h => h.CamaId == camaId && h.EstaActiva);

    public Task<AsignacionDispositivo?> ObtenerAsignacionVigenteAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        Task.FromResult(Asignaciones.SingleOrDefault(a => a.HospitalizacionId == hospitalizacionId && a.EstaVigente));

    public Task<string?> ObtenerCamaConDispositivoAsync(Guid dispositivoId, CancellationToken ct = default) =>
        Task.FromResult(CamaDe(dispositivoId));

    public string? CamaDe(Guid dispositivoId)
    {
        var asignacion = Asignaciones.SingleOrDefault(a => a.DispositivoId == dispositivoId && a.EstaVigente);
        var hospitalizacion = Hospitalizaciones.SingleOrDefault(h => h.Id == asignacion?.HospitalizacionId);
        return Camas.Camas.SingleOrDefault(c => c.Id == hospitalizacion?.CamaId)?.Codigo;
    }

    public Task<HospitalizacionActivaLectura?> ObtenerLecturaActivaAsync(Guid pacienteId, CancellationToken ct = default) =>
        Task.FromResult(Lecturas().SingleOrDefault(l => l.PacienteId == pacienteId));

    public Task<Pagina<HospitalizacionActivaLectura>> ListarActivasAsync(int pagina, int tamano, string? buscar, CancellationToken ct = default)
    {
        var filtradas = Lecturas()
            .Where(l => string.IsNullOrEmpty(buscar)
                        || l.NombreCompleto.Contains(buscar, StringComparison.OrdinalIgnoreCase)
                        || l.CodigoCama.Contains(buscar, StringComparison.OrdinalIgnoreCase)
                        || l.NumeroDocumento == buscar.ToUpperInvariant())
            .OrderBy(l => l.CodigoCama)
            .ToList();
        return Task.FromResult(new Pagina<HospitalizacionActivaLectura>(
            filtradas.Skip((pagina - 1) * tamano).Take(tamano).ToList(), pagina, tamano, filtradas.Count));
    }

    public Task<IReadOnlyList<HospitalizacionActivaLectura>> ListarActivasConDispositivoAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HospitalizacionActivaLectura>>(Lecturas().Where(l => l.DispositivoId is not null).OrderBy(l => l.CodigoCama).ToList());

    public Task<HospitalizacionPorDispositivoDto?> ObtenerActivaPorCodigoDispositivoAsync(string codigoDispositivo, CancellationToken ct = default)
    {
        var resultado =
            from d in Dispositivos.Dispositivos
            where d.Codigo == codigoDispositivo
            join a in Asignaciones.Where(a => a.EstaVigente) on d.Id equals a.DispositivoId
            join h in Hospitalizaciones.Where(h => h.EstaActiva) on a.HospitalizacionId equals h.Id
            select new HospitalizacionPorDispositivoDto(h.Id, h.PacienteId, h.CamaId, d.Id, a.Id, a.AsignadoEn);
        return Task.FromResult(resultado.SingleOrDefault());
    }

    public void Agregar(Hospitalizacion hospitalizacion) => Hospitalizaciones.Add(hospitalizacion);

    public void AgregarAsignacion(AsignacionDispositivo asignacion) => Asignaciones.Add(asignacion);

    private IEnumerable<HospitalizacionActivaLectura> Lecturas() =>
        from h in Hospitalizaciones.Where(h => h.EstaActiva)
        join p in Pacientes.Pacientes on h.PacienteId equals p.Id
        join c in Camas.Camas on h.CamaId equals c.Id
        let a = Asignaciones.SingleOrDefault(a => a.HospitalizacionId == h.Id && a.EstaVigente)
        let d = a is null ? null : Dispositivos.Dispositivos.Single(d => d.Id == a.DispositivoId)
        select new HospitalizacionActivaLectura(
            p.Id, p.NombreCompleto, p.TipoDocumento, p.NumeroDocumento, p.FechaNacimiento, p.FechaNacimientoEstimada,
            h.Id, h.DiagnosticoIngreso, h.IngresoEn, c.Id, c.Codigo, c.Servicio, d?.Id, d?.Codigo, a?.AsignadoEn);
}
