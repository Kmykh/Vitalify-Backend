using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;
using Vitalify.Infrastructure.Persistence.Historial.Configuraciones;

namespace Vitalify.Infrastructure.Persistence.Historial;

/// <summary>
/// Guarda lectura, estado e incidencias en una sola transacción. Traduce la clave repetida de
/// <c>lectura_signos</c> (mismo dispositivo y marca de tiempo) a <c>lectura-duplicada</c>, y las escrituras
/// concurrentes del estado (clave o xmin) a <c>conflicto-concurrencia</c>, que el caso de uso reintenta.
/// </summary>
internal sealed class UnidadDeTrabajoHistorial(HistorialDbContext db) : IUnidadDeTrabajoHistorial
{
    public async Task<Resultado<Unidad>> GuardarCambiosAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Unidad.Valor;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return ErroresComunes.Concurrencia;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            db.ChangeTracker.Clear();
            return pg.ConstraintName switch
            {
                ConfiguracionLecturaSignos.ClavePrimaria => ErroresTelemetria.LecturaDuplicada,
                ConfiguracionEstadoSignosActual.ClavePrimaria => ErroresComunes.Concurrencia,
                _ => Error.Conflicto("conflicto", "El registro ya existe."),
            };
        }
    }
}
