using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Sesiones;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioSesiones(TransaccionalDbContext db) : IRepositorioSesiones
{
    public Task<SesionRefresco?> ObtenerPorHashAsync(string hashToken, CancellationToken ct = default) =>
        db.SesionesRefresco.SingleOrDefaultAsync(s => s.HashToken == hashToken, ct);

    public async Task<IReadOnlyList<SesionRefresco>> ListarNoRevocadasAsync(Guid usuarioId, CancellationToken ct = default) =>
        await db.SesionesRefresco.Where(s => s.UsuarioId == usuarioId && s.RevocadaEn == null).ToListAsync(ct);

    public void Agregar(SesionRefresco sesion) => db.SesionesRefresco.Add(sesion);
}
