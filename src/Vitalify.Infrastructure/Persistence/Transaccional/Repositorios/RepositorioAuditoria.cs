using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioAuditoria(TransaccionalDbContext db) : IRepositorioAuditoria
{
    public void Agregar(RegistroAuditoria registro) => db.Auditoria.Add(registro);

    public async Task<Pagina<RegistroAuditoria>> ListarAsync(FiltroAuditoria filtro, int pagina, int tamano, CancellationToken ct = default)
    {
        var registros = db.Auditoria.AsNoTracking();
        if (filtro.Accion is { } accion)
        {
            registros = registros.Where(r => r.Accion == accion);
        }

        if (filtro.UsuarioId is { } usuarioId)
        {
            registros = registros.Where(r => r.UsuarioId == usuarioId);
        }

        if (filtro.Desde is { } desde)
        {
            registros = registros.Where(r => r.Fecha >= desde);
        }

        if (filtro.Hasta is { } hasta)
        {
            registros = registros.Where(r => r.Fecha <= hasta);
        }

        var total = await registros.CountAsync(ct);
        var filas = await registros
            .OrderByDescending(r => r.Fecha).ThenBy(r => r.Id)
            .Skip((pagina - 1) * tamano).Take(tamano)
            .ToListAsync(ct);
        return new Pagina<RegistroAuditoria>(filas, pagina, tamano, total);
    }
}
