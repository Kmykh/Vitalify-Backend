using System.Text;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Vitalify.Application.Clinica;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Api.Entrada.Mqtt;

/// <summary>
/// Adaptador de entrada real de la telemetría: se conecta al Mosquitto del proyecto IoT y recibe lo que publica cada
/// wearable ESP32.
/// <list type="bullet">
/// <item><c>device/{codigo}/telemetria</c> (QoS 1): JSON del contrato → <see cref="MensajeTelemetria"/> →
/// <see cref="IRegistrarLectura"/>, el mismo puerto de siempre. El código del JSON debe coincidir con el del tópico (el
/// ACL del broker garantiza que cada wearable solo publica en su propio tópico).</item>
/// <item><c>device/{codigo}/estado</c> (retenido): <c>online</c>/<c>offline</c> → <see cref="RegistrarPresenciaDispositivo"/>.</item>
/// </list>
/// Sesión persistente con client id fijo: lo que publiquen los wearables mientras el backend está caído queda en el
/// broker y llega al reconectar. Reconexión con espera creciente (2 a 30 s).
/// </summary>
public sealed class ReceptorMqtt(
    OpcionesMqtt opciones,
    IServiceScopeFactory scopes,
    RegistrarPresenciaDispositivo registrarPresencia,
    ILogger<ReceptorMqtt> logger) : BackgroundService
{
    private static readonly TimeSpan[] EsperasReintento = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

    private volatile bool _conectado;

    public bool Conectado => _conectado;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opciones.Habilitado)
        {
            logger.LogInformation("Receptor MQTT deshabilitado (Mqtt__Habilitado=false).");
            return;
        }

        var fabrica = new MqttFactory();
        using var cliente = fabrica.CreateMqttClient();
        cliente.ApplicationMessageReceivedAsync += e => AlRecibirAsync(e, stoppingToken);
        cliente.DisconnectedAsync += e =>
        {
            if (_conectado)
            {
                logger.LogWarning("MQTT: se perdió la conexión con {Servidor}:{Puerto} ({Motivo}).", opciones.Servidor, opciones.Puerto, e.Reason);
            }

            _conectado = false;
            return Task.CompletedTask;
        };

        var conexion = CrearOpcionesConexion();
        var suscripcion = fabrica.CreateSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(OpcionesMqtt.TopicoTelemetria).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .WithTopicFilter(f => f.WithTopic(OpcionesMqtt.TopicoEstado).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();

        var espera = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!cliente.IsConnected)
                {
                    var resultado = await cliente.ConnectAsync(conexion, stoppingToken);
                    await cliente.SubscribeAsync(suscripcion, stoppingToken);
                    _conectado = true;
                    espera = TimeSpan.FromSeconds(2);
                    logger.LogInformation(
                        "MQTT conectado a {Servidor}:{Puerto} como {ClientId} (sesión previa: {SesionPrevia}); suscrito a {Telemetria} y {Estado}.",
                        opciones.Servidor, opciones.Puerto, opciones.ClientId, resultado.IsSessionPresent,
                        OpcionesMqtt.TopicoTelemetria, OpcionesMqtt.TopicoEstado);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _conectado = false;
                logger.LogWarning(
                    "MQTT: no se pudo conectar a {Servidor}:{Puerto} ({Error}). Reintento en {Espera} s. ¿Está corriendo el broker del proyecto IoT?",
                    opciones.Servidor, opciones.Puerto, ex.Message, espera.TotalSeconds);
                try
                {
                    await Task.Delay(espera, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                espera = TimeSpan.FromSeconds(Math.Min(espera.TotalSeconds * 2, 30));
            }
        }

        if (cliente.IsConnected)
        {
            await cliente.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None);
        }
    }

    private MqttClientOptions CrearOpcionesConexion()
    {
        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(opciones.Servidor, opciones.Puerto)
            .WithClientId(opciones.ClientId)
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession(!opciones.SesionPersistente)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(TimeSpan.FromSeconds(10));

        if (opciones.Usuario is not null)
        {
            builder = builder.WithCredentials(opciones.Usuario, opciones.Clave);
        }

        return builder.Build();
    }

    private async Task AlRecibirAsync(MqttApplicationMessageReceivedEventArgs e, CancellationToken ct)
    {
        var partes = e.ApplicationMessage.Topic.Split('/');
        if (partes.Length != 3 || partes[0] != "device")
        {
            return;
        }

        var codigo = partes[1].Trim().ToUpperInvariant();
        var payload = e.ApplicationMessage.PayloadSegment;

        if (partes[2] == "estado")
        {
            var texto = Encoding.UTF8.GetString(payload);
            if (registrarPresencia.Ejecutar(codigo, texto))
            {
                logger.LogInformation("MQTT: {Dispositivo} está {Estado}.", codigo, texto.Trim().ToLowerInvariant());
            }

            return;
        }

        if (partes[2] == "telemetria")
        {
            await ProcesarTelemetriaAsync(codigo, payload, ct);
        }
    }

    private async Task ProcesarTelemetriaAsync(string codigoTopico, ArraySegment<byte> payload, CancellationToken ct)
    {
        var interpretada = MensajeTelemetria.Interpretar(payload.AsSpan(), OrigenLectura.Mqtt);
        if (!interpretada.EsExito)
        {
            logger.LogWarning(
                "MQTT: mensaje de {Dispositivo} que no cumple el contrato (campos: {Campos}).",
                codigoTopico, string.Join(", ", interpretada.Error.Detalles.Keys));
            return;
        }

        var lectura = interpretada.Valor;
        if (lectura.CodigoDispositivo != codigoTopico)
        {
            logger.LogWarning(
                "MQTT: el tópico es de {Topico} pero el mensaje dice {Mensaje}; se ignora.", codigoTopico, lectura.CodigoDispositivo);
            return;
        }

        for (var intento = 0; ; intento++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var resultado = await scope.ServiceProvider.GetRequiredService<IRegistrarLectura>().EjecutarAsync(lectura, ct);
                if (!resultado.EsExito)
                {
                    logger.LogWarning("MQTT: {Dispositivo} seq {Seq} no se registró ({Codigo}).", lectura.CodigoDispositivo, lectura.Seq, resultado.Error.Codigo);
                }
                else if (resultado.Valor.Estado == nameof(EstadoIngesta.Rechazada))
                {
                    logger.LogInformation(
                        "MQTT: {Dispositivo} seq {Seq} rechazada ({Incidencias}).",
                        lectura.CodigoDispositivo, lectura.Seq, string.Join(", ", resultado.Valor.Incidencias.Select(i => i.Tipo)));
                }
                else
                {
                    logger.LogDebug("MQTT: {Dispositivo} seq {Seq} -> {Estado}", lectura.CodigoDispositivo, lectura.Seq, resultado.Valor.Estado);
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && intento < EsperasReintento.Length)
            {
                logger.LogWarning("MQTT: error al registrar {Dispositivo} seq {Seq}; reintento {Intento}.", lectura.CodigoDispositivo, lectura.Seq, intento + 1);
                await Task.Delay(EsperasReintento[intento], ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // El broker ya entregó el mensaje: si la base sigue sin responder, la lectura se pierde y queda en el log.
                logger.LogError(ex, "MQTT: no se pudo registrar {Dispositivo} seq {Seq} tras {Intentos} intentos.", lectura.CodigoDispositivo, lectura.Seq, intento + 1);
                return;
            }
        }
    }
}

/// <summary><c>/health</c> incluye <c>mqtt</c> cuando el receptor está habilitado.</summary>
internal sealed class ChequeoMqtt(ReceptorMqtt receptor, OpcionesMqtt opciones) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(receptor.Conectado
            ? HealthCheckResult.Healthy($"Conectado a {opciones.Servidor}:{opciones.Puerto}.")
            : HealthCheckResult.Unhealthy($"Sin conexión con el broker {opciones.Servidor}:{opciones.Puerto}."));
}

internal static class RegistroMqtt
{
    public static IServiceCollection AddReceptorMqtt(this IServiceCollection services, IConfiguration configuration)
    {
        var opciones = OpcionesMqtt.Cargar(configuration);
        services.AddSingleton(opciones);
        services.AddSingleton<ReceptorMqtt>();
        services.AddHostedService(sp => sp.GetRequiredService<ReceptorMqtt>());

        if (opciones.Habilitado)
        {
            services.AddHealthChecks().AddCheck<ChequeoMqtt>("mqtt", tags: ["mqtt"]);
        }

        return services;
    }
}
