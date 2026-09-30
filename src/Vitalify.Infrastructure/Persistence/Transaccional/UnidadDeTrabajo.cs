using Microsoft.EntityFrameworkCore;
using Npgsql;
using Vitalify.Application.Camas;
using Vitalify.Application.Comun;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Application.Usuarios;
using Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

namespace Vitalify.Infrastructure.Persistence.Transaccional;

/// <summary>
/// Guarda todos los cambios pendientes en una transacción. Las invariantes que también garantiza la base (índices
/// únicos, incluidos los parciales) se traducen a un <see cref="TipoError.Conflicto"/> con su propio código: así se
/// cubren las carreras entre dos usuarios que la validación previa del caso de uso no puede ver.
/// </summary>
internal sealed class UnidadDeTrabajo(TransaccionalDbContext db) : IUnidadDeTrabajo
{
    private static readonly Dictionary<string, Error> ConflictosPorIndice = new()
    {
        [ConfiguracionUsuario.IndiceCorreo] = ErroresUsuario.CorreoEnUso,
        [ConfiguracionCama.IndiceCodigo] = ErroresCama.CodigoDuplicado,
        [ConfiguracionDispositivo.IndiceCodigo] = ErroresDispositivo.CodigoDuplicado,
        [ConfiguracionPaciente.IndiceDocumento] = ErroresPaciente.DocumentoDuplicado,
        [ConfiguracionHospitalizacion.IndiceCamaActiva] = ErroresCama.Ocupada,
        [ConfiguracionHospitalizacion.IndicePacienteActivo] = ErroresPaciente.YaHospitalizado,
        [ConfiguracionAsignacionDispositivo.IndiceDispositivoVigente] = ErroresDispositivo.YaVinculado,
        [ConfiguracionAsignacionDispositivo.IndiceHospitalizacionVigente] = ErroresPaciente.YaTieneDispositivo,
    };

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
            // Descarta los cambios fallidos para que el contexto siga usable en esta petición.
            db.ChangeTracker.Clear();

            return pg.ConstraintName is { } indice && ConflictosPorIndice.TryGetValue(indice, out var error)
                ? error
                : Error.Conflicto("conflicto", "El recurso ya existe.");
        }
    }
}
