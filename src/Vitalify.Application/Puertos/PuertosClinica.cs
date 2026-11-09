using Vitalify.Domain.Clinica;

namespace Vitalify.Application.Puertos;

/// <summary>Evaluaciones de riesgo (historial). Se guardan con <see cref="IUnidadDeTrabajoHistorial"/>.</summary>
public interface IRepositorioEvaluaciones
{
    void Agregar(EvaluacionRiesgo evaluacion);

    Task<EvaluacionRiesgo?> ObtenerUltimaAsync(Guid hospitalizacionId, CancellationToken ct = default);

    Task<EvaluacionRiesgo?> ObtenerPorIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, EvaluacionRiesgo>> ObtenerUltimasAsync(IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default);

    /// <summary>Evaluaciones de la hospitalización entre ambos límites (inclusive), de la más antigua a la más reciente.</summary>
    Task<IReadOnlyList<EvaluacionRiesgo>> ListarAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct = default);

    /// <summary>Evaluaciones agrupadas cada <paramref name="segundos"/>, del grupo más antiguo al más reciente. Se agrega en la base.</summary>
    Task<IReadOnlyList<GrupoEvaluaciones>> AgruparAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, int segundos, CancellationToken ct = default);

    /// <summary>Primero, máximo y último NEWS2 y MEWS de cada hospitalización entre ambos instantes (entrega de turno).</summary>
    Task<IReadOnlyDictionary<Guid, TendenciaRiesgo>> ObtenerTendenciasAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, DateTime desde, DateTime hasta, CancellationToken ct = default);
}

public sealed record TendenciaPuntaje(int Primero, int Maximo, int Ultimo);

public sealed record TendenciaRiesgo(TendenciaPuntaje News2, TendenciaPuntaje Mews, int Evaluaciones);

/// <summary>Observaciones del personal clínico (historial). Se guardan con <see cref="IUnidadDeTrabajoHistorial"/>.</summary>
public interface IRepositorioObservaciones
{
    void Agregar(ObservacionEnfermeria observacion);

    /// <summary>Observaciones de la hospitalización tomadas desde <paramref name="desde"/>, la más reciente primero.</summary>
    Task<IReadOnlyList<ObservacionEnfermeria>> ListarDesdeAsync(Guid hospitalizacionId, DateTime desde, CancellationToken ct = default);

    /// <summary>Observaciones de la hospitalización tomadas entre ambos límites (inclusive), la más reciente primero.</summary>
    Task<IReadOnlyList<ObservacionEnfermeria>> ListarAsync(Guid hospitalizacionId, DateTime desde, DateTime hasta, CancellationToken ct = default);

    /// <summary>La toma manual más reciente de la hospitalización, de cualquier antigüedad.</summary>
    Task<ObservacionEnfermeria?> ObtenerUltimaTomaAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>Brazo de la última toma de presión que lo registró, para sugerirlo en la siguiente.</summary>
    Task<BrazoPresion?> ObtenerUltimoBrazoPresionAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>Por hospitalización, la última toma con preocupación clínica.</summary>
    Task<IReadOnlyDictionary<Guid, ObservacionEnfermeria>> ObtenerUltimasPreocupacionesAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default);
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

    /// <summary>Códigos de los dispositivos cuyo último estado es <c>online</c>.</summary>
    IReadOnlyList<string> ListarEnLinea();
}
