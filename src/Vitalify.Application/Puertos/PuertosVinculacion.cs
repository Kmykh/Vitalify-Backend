using Vitalify.Application.Vinculacion;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Application.Puertos;

/// <summary>
/// Envía comandos al wearable por <c>device/{codigo}/comandos</c> (QoS 1). El adaptador MQTT usa la misma conexión que
/// recibe la telemetría. Publicar es "dejarlo encargado": no lanza excepciones; si el broker no está disponible, el
/// adaptador lo registra en el log y el comando retenido se vuelve a generar cuando el wearable se reconecta.
/// </summary>
public interface IPublicadorComandosDispositivo
{
    /// <param name="retenido">El broker guarda el último comando retenido y se lo entrega al wearable al reconectarse.</param>
    Task PublicarAsync(string codigoDispositivo, ComandoDispositivo comando, bool retenido, CancellationToken ct = default);
}

/// <summary>Códigos de vinculación por QR (esquema <c>vitalify</c>). Se guardan con <see cref="IUnidadDeTrabajo"/>.</summary>
public interface IRepositorioCodigosVinculacion
{
    /// <summary>El código ni usado ni invalidado del dispositivo (puede estar vencido), o null.</summary>
    Task<CodigoVinculacion?> ObtenerActivoAsync(Guid dispositivoId, CancellationToken ct = default);

    /// <summary>Por el SHA-256 del token de vinculación.</summary>
    Task<CodigoVinculacion?> ObtenerPorHashTokenAsync(string hashToken, CancellationToken ct = default);

    void Agregar(CodigoVinculacion codigo);
}
