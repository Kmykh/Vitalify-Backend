using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioCodigosVinculacion(TransaccionalDbContext db) : IRepositorioCodigosVinculacion
{
    public Task<CodigoVinculacion?> ObtenerActivoAsync(Guid dispositivoId, CancellationToken ct = default) =>
        db.CodigosVinculacion.SingleOrDefaultAsync(c => c.DispositivoId == dispositivoId && c.UsadoEn == null && c.InvalidadoEn == null, ct);

    public Task<CodigoVinculacion?> ObtenerPorHashTokenAsync(string hashToken, CancellationToken ct = default) =>
        db.CodigosVinculacion.SingleOrDefaultAsync(c => c.TokenHash == hashToken, ct);

    public void Agregar(CodigoVinculacion codigo) => db.CodigosVinculacion.Add(codigo);
}
