using Vitalify.Application.Comun;

namespace Vitalify.Application.Puertos;

public interface IUnidadDeTrabajo
{
    /// <summary>
    /// Persiste los cambios pendientes de todos los repositorios en una sola transacción.
    /// Una violación de unicidad (por ejemplo, dos registros simultáneos con el mismo correo)
    /// se devuelve como <see cref="TipoError.Conflicto"/>.
    /// </summary>
    Task<Resultado<Unidad>> GuardarCambiosAsync(CancellationToken ct = default);
}
