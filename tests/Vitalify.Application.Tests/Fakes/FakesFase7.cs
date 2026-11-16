using Vitalify.Application.Puertos;
using Vitalify.Domain.Notas;

namespace Vitalify.Application.Tests.Fakes;

public sealed class RepositorioNotasEnMemoria : IRepositorioNotas
{
    public List<NotaClinica> Notas { get; } = [];
    public List<NotaClinicaVersion> Versiones { get; } = [];

    public Task<NotaClinica?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Notas.SingleOrDefault(n => n.Id == id));

    public Task<IReadOnlyList<NotaClinica>> ListarDeHospitalizacionAsync(Guid hospitalizacionId, int? limite = null, CancellationToken ct = default)
    {
        var notas = Notas.Where(n => n.HospitalizacionId == hospitalizacionId).OrderByDescending(n => n.CreadaEn);
        return Task.FromResult<IReadOnlyList<NotaClinica>>((limite is { } l ? notas.Take(l) : notas).ToList());
    }

    public Task<IReadOnlyList<NotaClinicaVersion>> ListarVersionesAsync(Guid notaId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NotaClinicaVersion>>(Versiones.Where(v => v.NotaId == notaId).OrderByDescending(v => v.Numero).ToList());

    public void Agregar(NotaClinica nota) => Notas.Add(nota);

    public void AgregarVersion(NotaClinicaVersion version) => Versiones.Add(version);

    public Task<NotaClinica?> ObtenerDeTurnoAsync(Guid hospitalizacionId, string guardia, CancellationToken ct = default) =>
        Task.FromResult(Notas.SingleOrDefault(n => n.HospitalizacionId == hospitalizacionId && n.Tipo == TipoNota.ProcesoEnfermeria && n.Guardia == guardia));

    public Task<IReadOnlyList<(Guid HospitalizacionId, string Guardia)>> ListarGuardiasConNotaTurnoAsync(
        DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(Guid, string)>>(Notas
            .Where(n => n.Tipo == TipoNota.ProcesoEnfermeria && n.Guardia is not null && n.CreadaEn >= desde && n.CreadaEn <= hasta)
            .Select(n => (n.HospitalizacionId, n.Guardia!)).ToList());
}
