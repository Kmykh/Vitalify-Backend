using Microsoft.Extensions.Logging;
using Vitalify.Application.Puertos;
using Vitalify.Application.Vinculacion;

namespace Vitalify.Infrastructure.Vinculacion;

/// <summary>
/// Adaptador para cuando MQTT está deshabilitado (<c>Mqtt__Habilitado=false</c>): solo registra en el log qué comando
/// se habría enviado. Nunca escribe el contenido del QR ni datos del paciente: solo la acción y el dispositivo.
/// </summary>
internal sealed class PublicadorComandosEnLog(ILogger<PublicadorComandosEnLog> logger) : IPublicadorComandosDispositivo
{
    public Task PublicarAsync(string codigoDispositivo, ComandoDispositivo comando, bool retenido, CancellationToken ct = default)
    {
        logger.LogInformation(
            "MQTT deshabilitado: no se publica {Accion} a {Dispositivo} (retenido: {Retenido}).", comando.Accion, codigoDispositivo, retenido);
        return Task.CompletedTask;
    }
}
