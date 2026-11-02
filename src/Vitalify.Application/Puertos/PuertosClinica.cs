using Vitalify.Domain.Clinica;

namespace Vitalify.Application.Puertos;

/// <summary>Evaluaciones de riesgo (historial). Se guardan con <see cref="IUnidadDeTrabajoHistorial"/>.</summary>
public interface IRepositorioEvaluaciones
{
    void Agregar(EvaluacionRiesgo evaluacion);

    Task<EvaluacionRiesgo?> ObtenerUltimaAsync(Guid hospitalizacionId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, EvaluacionRiesgo>> ObtenerUltimasAsync(IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default);
}

/// <summary>Observaciones del personal clínico (historial). Se guardan con <see cref="IUnidadDeTrabajoHistorial"/>.</summary>
public interface IRepositorioObservaciones
{
    void Agregar(ObservacionEnfermeria observacion);

    /// <summary>Observaciones de la hospitalización tomadas desde <paramref name="desde"/>, la más reciente primero.</summary>
    Task<IReadOnlyList<ObservacionEnfermeria>> ListarDesdeAsync(Guid hospitalizacionId, DateTime desde, CancellationToken ct = default);
}

public sealed record PresenciaDispositivo(bool EnLinea, DateTime Desde);

/// <summary>
/// Si cada wearable está en línea, según el tópico retenido <c>device/{codigo}/estado</c> (<c>online</c>/<c>offline</c>;
/// el <c>offline</c> lo publica el broker como "last will" cuando el ESP32 desaparece). Es estado en memoria: tras
/// reiniciar la API, el broker vuelve a entregar el último valor retenido.
/// </summary>
public interface IPresenciaDispositivos
{
    void Registrar(string codigoDispositivo, PresenciaDispositivo presencia);

    PresenciaDispositivo? Obtener(string codigoDispositivo);
}
