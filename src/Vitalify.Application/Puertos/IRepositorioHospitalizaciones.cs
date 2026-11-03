using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Vinculacion;
using Vitalify.Domain.Hospitalizaciones;

namespace Vitalify.Application.Puertos;

/// <summary>Hospitalizaciones y asignaciones de dispositivos (el historial de qué sensor estuvo con quién).</summary>
public interface IRepositorioHospitalizaciones
{
    Task<Hospitalizacion?> ObtenerActivaDePacienteAsync(Guid pacienteId, CancellationToken ct = default);

    Task<Hospitalizacion?> ObtenerPorIdAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>La hospitalización más reciente del paciente (la activa, si la hay), o null si nunca estuvo hospitalizado.</summary>
    Task<Hospitalizacion?> ObtenerUltimaDePacienteAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Todos los episodios del paciente, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<EpisodioLectura>> ListarEpisodiosAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Sensores que estuvieron vinculados a esas hospitalizaciones, en orden de vinculación.</summary>
    Task<IReadOnlyList<SensorEpisodioLectura>> ListarSensoresAsync(IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default);

    Task<bool> CamaOcupadaAsync(Guid camaId, CancellationToken ct = default);

    Task<AsignacionDispositivo?> ObtenerAsignacionVigenteAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>Código de la cama cuya hospitalización tiene vinculado el dispositivo, o null.</summary>
    Task<string?> ObtenerCamaConDispositivoAsync(Guid dispositivoId, CancellationToken ct = default);

    /// <summary>
    /// Cama y nombre del paciente con el que está vinculado el dispositivo, o null. El nombre solo se usa para
    /// calcular las iniciales que muestra el wearable: nunca se registra.
    /// </summary>
    Task<DispositivoEnCama?> ObtenerVinculoDispositivoAsync(Guid dispositivoId, CancellationToken ct = default);

    Task<HospitalizacionActivaLectura?> ObtenerLecturaActivaAsync(Guid pacienteId, CancellationToken ct = default);

    /// <summary>Paciente, servicio y cama de una hospitalización (para las alertas y los eventos), o null.</summary>
    Task<ContextoHospitalizacion?> ObtenerContextoAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>Hospitalizaciones activas (con o sin sensor); con <paramref name="servicioId"/>, solo las de ese servicio.</summary>
    Task<IReadOnlyList<HospitalizacionActivaLectura>> ListarActivasDeServicioAsync(Guid? servicioId, CancellationToken ct = default);

    Task<Pagina<HospitalizacionActivaLectura>> ListarActivasAsync(int pagina, int tamano, string? buscar, CancellationToken ct = default);

    Task<IReadOnlyList<HospitalizacionActivaLectura>> ListarActivasConDispositivoAsync(CancellationToken ct = default);

    Task<HospitalizacionPorDispositivoDto?> ObtenerActivaPorCodigoDispositivoAsync(string codigoDispositivo, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListarCodigosDispositivosVinculadosAsync(CancellationToken ct = default);

    void Agregar(Hospitalizacion hospitalizacion);

    void AgregarAsignacion(AsignacionDispositivo asignacion);
}
