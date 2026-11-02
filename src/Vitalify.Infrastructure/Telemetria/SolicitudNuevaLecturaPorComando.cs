using Vitalify.Application.Puertos;
using Vitalify.Application.Vinculacion;

namespace Vitalify.Infrastructure.Telemetria;

/// <summary>
/// Pide al wearable que repita la medición con el comando <c>repetir-medicion</c> (no retenido) por
/// <see cref="IPublicadorComandosDispositivo"/>. La incidencia ya queda en la tabla con <c>requiere_nueva_lectura</c>.
/// </summary>
internal sealed class SolicitudNuevaLecturaPorComando(IPublicadorComandosDispositivo publicador) : ISolicitudNuevaLectura
{
    /// <summary>Hoy la única variable que se pide repetir es la presión (llegó la sistólica sin la diastólica, o al revés).</summary>
    public const string VariablePresion = "presion";

    public Task SolicitarAsync(SolicitudNuevaLectura solicitud, CancellationToken ct = default) =>
        publicador.PublicarAsync(solicitud.CodigoDispositivo, new RepetirMedicion(VariablePresion, solicitud.Motivo), retenido: false, ct);
}
