using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Pacientes;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioPacientes(TransaccionalDbContext db) : IRepositorioPacientes
{
    public async Task<Paciente?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) => await db.Pacientes.FindAsync([id], ct);

    public Task<Paciente?> ObtenerPorDocumentoAsync(TipoDocumento tipo, string numero, CancellationToken ct = default) =>
        db.Pacientes.SingleOrDefaultAsync(p => p.TipoDocumento == tipo && p.NumeroDocumento == numero, ct);

    public void Agregar(Paciente paciente) => db.Pacientes.Add(paciente);
}
