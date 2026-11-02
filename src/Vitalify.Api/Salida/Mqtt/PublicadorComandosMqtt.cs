using System.Threading.Channels;
using Vitalify.Application.Puertos;
using Vitalify.Application.Vinculacion;

namespace Vitalify.Api.Salida.Mqtt;

/// <summary>Un comando listo para publicar en <c>device/{codigo}/comandos</c>.</summary>
public sealed record MensajeComando(string Dispositivo, string Accion, string Topico, byte[] Payload, bool Retenido);

/// <summary>
/// Cola de comandos salientes. Los casos de uso encolan y siguen (no esperan al broker); el <c>ReceptorMqtt</c>, dueño
/// de la única conexión MQTT del backend, la vacía en orden. Así nunca se espera un PUBACK dentro del manejador de
/// mensajes de MQTTnet y "desvinculado" siempre sale antes que el "mostrar-qr" siguiente. Si el broker no está, los
/// comandos esperan a la reconexión; con 1000 en espera se descartan los más antiguos.
/// </summary>
public sealed class ColaComandosMqtt
{
    public const int Capacidad = 1000;

    private readonly Channel<MensajeComando> _canal = Channel.CreateBounded<MensajeComando>(
        new BoundedChannelOptions(Capacidad) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<MensajeComando> Lector => _canal.Reader;

    public bool Encolar(MensajeComando mensaje) => _canal.Writer.TryWrite(mensaje);
}

/// <summary>Adaptador de salida de <see cref="IPublicadorComandosDispositivo"/> por MQTT (QoS 1).</summary>
public sealed class PublicadorComandosMqtt(ColaComandosMqtt cola, ILogger<PublicadorComandosMqtt> logger) : IPublicadorComandosDispositivo
{
    public Task PublicarAsync(string codigoDispositivo, ComandoDispositivo comando, bool retenido, CancellationToken ct = default)
    {
        var mensaje = new MensajeComando(
            codigoDispositivo, comando.Accion, ContratoComandos.Topico(codigoDispositivo), ContratoComandos.Serializar(comando), retenido);
        if (!cola.Encolar(mensaje))
        {
            logger.LogWarning("MQTT: no se pudo encolar {Accion} para {Dispositivo}.", comando.Accion, codigoDispositivo);
        }

        return Task.CompletedTask;
    }
}
