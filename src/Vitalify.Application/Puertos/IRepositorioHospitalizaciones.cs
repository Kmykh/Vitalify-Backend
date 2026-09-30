using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Application.Puertos;

/// <summary>Hospitalizaciones y asignaciones de dispositivos (el historial de qué sensor estuvo con quién).</summary>
public interface IRepositorioHospitalizaciones
{
    Task<Hospitalizacion?> ObtenerActivaDePacienteAsync(Guid pacienteId, CancellationToken ct = default);

    Task<bool> CamaOcupadaAsync(Guid camaId, CancellationToken ct = default);

    Task<AsignacionDispositivo?> ObtenerAsignacionVigenteAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>Código de la cama cuya hospitalización tiene vinculado el dispositivo, o null.</summary>
    Task<string?> ObtenerCamaConDispositivoAsync(Guid dispositivoId, CancellationToken ct = default);

    Task<HospitalizacionActivaLectura?> ObtenerLecturaActivaAsync(Guid pacienteId, CancellationToken ct = default);

    Task<Pagina<HospitalizacionActivaLectura>> ListarActivasAsync(int pagina, int tamano, string? buscar, CancellationToken ct = default);

    Task<IReadOnlyList<HospitalizacionActivaLectura>> ListarActivasConDispositivoAsync(CancellationToken ct = default);

    Task<HospitalizacionPorDispositivoDto?> ObtenerActivaPorCodigoDispositivoAsync(string codigoDispositivo, CancellationToken ct = default);

    void Agregar(Hospitalizacion hospitalizacion);

    void AgregarAsignacion(AsignacionDispositivo asignacion);
}
