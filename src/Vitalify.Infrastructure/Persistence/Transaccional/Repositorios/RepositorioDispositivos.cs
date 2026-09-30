using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioDispositivos(TransaccionalDbContext db) : IRepositorioDispositivos
{
    public async Task<Dispositivo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) => await db.Dispositivos.FindAsync([id], ct);

    public Task<bool> ExisteCodigoAsync(string codigo, CancellationToken ct = default) =>
        db.Dispositivos.AnyAsync(d => d.Codigo == codigo, ct);

    public async Task<IReadOnlyList<DispositivoDto>> ListarAsync(EstadoDispositivo? estado, CancellationToken ct = default)
    {
        var dispositivos = db.Dispositivos.AsNoTracking();
        if (estado is { } filtro)
        {
            dispositivos = dispositivos.Where(d => d.Estado == filtro);
        }

        var filas = await dispositivos
            .OrderBy(d => d.Codigo)
            .Select(d => new
            {
                d.Id,
                d.Codigo,
                d.Descripcion,
                d.Estado,
                Cama = (from a in db.AsignacionesDispositivo
                        where a.DispositivoId == d.Id && a.LiberadoEn == null
                        join h in db.Hospitalizaciones on a.HospitalizacionId equals h.Id
                        where h.Estado == EstadoHospitalizacion.Activa
                        join c in db.Camas on h.CamaId equals c.Id
                        select c.Codigo).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return filas.Select(f => new DispositivoDto(f.Id, f.Codigo, f.Descripcion, f.Estado.ToString(), f.Cama)).ToList();
    }

    public void Agregar(Dispositivo dispositivo) => db.Dispositivos.Add(dispositivo);
}
