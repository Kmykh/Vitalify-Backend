using Vitalify.Domain.Auditoria;

namespace Vitalify.Application.Puertos;

public interface IRepositorioAuditoria
{
    void Agregar(RegistroAuditoria registro);
}
