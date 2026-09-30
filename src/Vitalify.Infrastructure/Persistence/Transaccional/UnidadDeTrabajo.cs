using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Usuarios;
using Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

namespace Vitalify.Infrastructure.Persistence.Transaccional;

internal sealed class UnidadDeTrabajo(TransaccionalDbContext db) : IUnidadDeTrabajo
{
    public async Task<Resultado<Unidad>> GuardarCambiosAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Unidad.Valor;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            // Descarta los cambios fallidos para que el contexto siga usable en esta petición.
            db.ChangeTracker.Clear();

            return pg.ConstraintName == ConfiguracionUsuario.IndiceCorreo
                ? ErroresUsuario.CorreoEnUso
                : Error.Conflicto("conflicto", "El recurso ya existe.");
        }
    }
}
