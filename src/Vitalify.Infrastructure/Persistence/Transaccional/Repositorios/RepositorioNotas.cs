using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Notas;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioNotas(TransaccionalDbContext db) : IRepositorioNotas
{
    public Task<NotaClinica?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) =>
        db.Notas.SingleOrDefaultAsync(n => n.Id == id, ct);

    public async Task<IReadOnlyList<NotaClinica>> ListarDeHospitalizacionAsync(Guid hospitalizacionId, int? limite = null, CancellationToken ct = default)
    {
        var notas = db.Notas.AsNoTracking()
            .Where(n => n.HospitalizacionId == hospitalizacionId)
            .OrderByDescending(n => n.CreadaEn).ThenBy(n => n.Id);
        return await (limite is { } l ? notas.Take(l) : notas).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<NotaClinicaVersion>> ListarVersionesAsync(Guid notaId, CancellationToken ct = default) =>
        await db.VersionesNotas.AsNoTracking()
            .Where(v => v.NotaId == notaId)
            .OrderByDescending(v => v.Numero)
            .ToListAsync(ct);

    public void Agregar(NotaClinica nota) => db.Notas.Add(nota);

    public void AgregarVersion(NotaClinicaVersion version) => db.VersionesNotas.Add(version);
}
