using Vitalify.Application.Comun;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Application.Puertos;

public interface IRepositorioAuditoria
{
    void Agregar(RegistroAuditoria registro);

    /// <summary>Registros filtrados, del más reciente al más antiguo.</summary>
    Task<Pagina<RegistroAuditoria>> ListarAsync(FiltroAuditoria filtro, int pagina, int tamano, CancellationToken ct = default);
}
