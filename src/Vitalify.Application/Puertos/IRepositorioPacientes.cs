using Vitalify.Domain.Pacientes;

namespace Vitalify.Application.Puertos;

public interface IRepositorioPacientes
{
    Task<Paciente?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    Task<Paciente?> ObtenerPorDocumentoAsync(TipoDocumento tipo, string numero, CancellationToken ct = default);

    void Agregar(Paciente paciente);
}
