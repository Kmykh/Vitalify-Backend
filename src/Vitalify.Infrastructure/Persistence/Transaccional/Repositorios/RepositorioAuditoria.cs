using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioAuditoria(TransaccionalDbContext db) : IRepositorioAuditoria
{
    public void Agregar(RegistroAuditoria registro) => db.Auditoria.Add(registro);
}
