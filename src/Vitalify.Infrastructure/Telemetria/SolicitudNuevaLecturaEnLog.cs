using Microsoft.Extensions.Logging;
using Vitalify.Application.Puertos;

namespace Vitalify.Infrastructure.Telemetria;

/// <summary>
/// Adaptador provisional: registra la solicitud en el log (la incidencia ya queda en la tabla con
/// <c>requiere_nueva_lectura</c>). En la fase 7 publicará un comando MQTT al dispositivo.
/// </summary>
internal sealed class SolicitudNuevaLecturaEnLog(ILogger<SolicitudNuevaLecturaEnLog> logger) : ISolicitudNuevaLectura
{
    public Task SolicitarAsync(SolicitudNuevaLectura solicitud, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Solicitud de nueva lectura al dispositivo {Dispositivo} (medición {MedidoEn:O}): {Motivo}",
            solicitud.CodigoDispositivo, solicitud.MedidoEn, solicitud.Motivo);
        return Task.CompletedTask;
    }
}
