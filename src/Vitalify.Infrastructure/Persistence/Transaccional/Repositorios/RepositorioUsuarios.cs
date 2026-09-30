using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioUsuarios(TransaccionalDbContext db) : IRepositorioUsuarios
{
    public async Task<Usuario?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Usuarios.FindAsync([id], ct);

    public Task<Usuario?> ObtenerPorCorreoAsync(Correo correo, CancellationToken ct = default) =>
        db.Usuarios.SingleOrDefaultAsync(u => u.Correo == correo, ct);

    public Task<bool> ExisteCorreoAsync(Correo correo, CancellationToken ct = default) =>
        db.Usuarios.AnyAsync(u => u.Correo == correo, ct);

    public Task<bool> ExisteConRolAsync(Rol rol, CancellationToken ct = default) =>
        db.Usuarios.AnyAsync(u => u.Rol == rol, ct);

    public async Task<Pagina<Usuario>> ListarAsync(int pagina, int tamano, CancellationToken ct = default)
    {
        var total = await db.Usuarios.CountAsync(ct);
        var elementos = await db.Usuarios.AsNoTracking()
            .OrderBy(u => u.Nombre).ThenBy(u => u.Id)
            .Skip((pagina - 1) * tamano).Take(tamano)
            .ToListAsync(ct);

        return new Pagina<Usuario>(elementos, pagina, tamano, total);
    }

    public void Agregar(Usuario usuario) => db.Usuarios.Add(usuario);
}
