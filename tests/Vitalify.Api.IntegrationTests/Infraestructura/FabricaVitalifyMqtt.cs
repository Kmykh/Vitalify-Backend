using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace Vitalify.Api.IntegrationTests.Infraestructura;

/// <summary>
/// La API con el receptor MQTT encendido contra un Mosquitto real (eclipse-mosquitto:2, la misma imagen del broker del
/// proyecto IoT). Las pruebas publican como lo hace el ESP32: QoS 1 en device/{codigo}/telemetria.
/// </summary>
public class FabricaVitalifyMqtt : FabricaVitalify
{
    private IContainer? _mosquitto;

    /// <summary>Con TLS, el broker escucha en 8883 con un certificado autofirmado generado por la prueba.</summary>
    protected virtual bool UsarTls => false;

    private int PuertoContenedor => UsarTls ? 8883 : 1883;

    private IContainer Mosquitto => _mosquitto ?? throw new InvalidOperationException("El broker todavía no arrancó.");

    public string HostBroker => Mosquitto.Hostname;

    public int PuertoBroker => Mosquitto.GetMappedPublicPort(PuertoContenedor);

    /// <summary>Opciones de cliente como las del wearable: con TLS acepta el certificado autofirmado de la prueba.</summary>
    private MqttClientOptionsBuilder OpcionesCliente(string clientId)
    {
        var opciones = new MqttClientOptionsBuilder().WithTcpServer(HostBroker, PuertoBroker).WithClientId(clientId);
        return UsarTls ? opciones.WithTlsOptions(t => t.UseTls().WithCertificateValidationHandler(_ => true)) : opciones;
    }

    /// <summary>Publica con QoS 1, como el firmware.</summary>
    public async Task PublicarAsync(string topico, string payload, bool retenido = false)
    {
        using var cliente = new MqttFactory().CreateMqttClient();
        await cliente.ConnectAsync(OpcionesCliente($"prueba-{Guid.NewGuid():N}").Build());
        await cliente.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topico)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retenido)
            .Build());
        await cliente.DisconnectAsync();
    }

    /// <summary>Se suscribe como lo haría el wearable y guarda lo que recibe (tópico, texto y si llegó como retenido).</summary>
    public async Task<SuscripcionPrueba> SuscribirAsync(string topico)
    {
        var cliente = new MqttFactory().CreateMqttClient();
        var suscripcion = new SuscripcionPrueba(cliente);
        cliente.ApplicationMessageReceivedAsync += e =>
        {
            suscripcion.Mensajes.Enqueue(new MensajeRecibido(
                e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment), e.ApplicationMessage.Retain));
            return Task.CompletedTask;
        };
        await cliente.ConnectAsync(OpcionesCliente($"pantalla-{Guid.NewGuid():N}").Build());
        await cliente.SubscribeAsync(new MqttFactory().CreateSubscribeOptionsBuilder()
            .WithTopicFilter(t => t.WithTopic(topico).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build());
        return suscripcion;
    }

    /// <summary>Espera hasta que la condición se cumpla (la ingesta por MQTT es asíncrona).</summary>
    public static async Task EsperarAsync(Func<Task<bool>> condicion, string descripcion, int segundos = 15)
    {
        var limite = DateTime.UtcNow.AddSeconds(segundos);
        while (DateTime.UtcNow < limite)
        {
            if (await condicion())
            {
                return;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"No se cumplió en {segundos} s: {descripcion}");
    }

    protected override async Task<IReadOnlyDictionary<string, string>> ConfiguracionAdicionalAsync()
    {
        var contenedor = new ContainerBuilder("eclipse-mosquitto:2")
            .WithPortBinding(PuertoContenedor, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("mosquitto version .* running"));
        if (UsarTls)
        {
            var (certificado, clave) = CertificadoAutofirmado();
            contenedor = contenedor
                .WithResourceMapping(Encoding.UTF8.GetBytes(
                    "listener 8883\ncertfile /mosquitto/config/cert.pem\nkeyfile /mosquitto/config/key.pem\nallow_anonymous true\npersistence false\n"),
                    "/mosquitto/config/mosquitto.conf")
                .WithResourceMapping(Encoding.UTF8.GetBytes(certificado), "/mosquitto/config/cert.pem")
                .WithResourceMapping(Encoding.UTF8.GetBytes(clave), "/mosquitto/config/key.pem");
        }
        else
        {
            contenedor = contenedor.WithResourceMapping(
                Encoding.UTF8.GetBytes("listener 1883\nallow_anonymous true\npersistence false\n"), "/mosquitto/config/mosquitto.conf");
        }

        _mosquitto = contenedor.Build();
        await _mosquitto.StartAsync();
        var configuracion = new Dictionary<string, string>
        {
            ["Mqtt__Habilitado"] = "true",
            ["Mqtt__Servidor"] = HostBroker,
            ["Mqtt__Puerto"] = PuertoBroker.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Mqtt__Usuario"] = "prueba",
            ["Mqtt__Clave"] = "prueba", // el broker de prueba acepta anónimos; nunca debe colarse la clave real del .env
            ["Mqtt__ClientId"] = $"vitalify-backend-pruebas-{Guid.NewGuid():N}",
            ["Mqtt__UsarTls"] = UsarTls ? "true" : "false",
            // Solo esta prueba (Development, certificado autofirmado) desactiva la validación del certificado.
            ["Mqtt__ValidarCertificado"] = UsarTls ? "false" : "true",
        };
        return configuracion;
    }

    protected override async Task LiberarAdicionalAsync()
    {
        if (_mosquitto is not null)
        {
            await _mosquitto.DisposeAsync();
        }
    }

    /// <summary>Certificado autofirmado para <c>localhost</c> (PEM del certificado y de la clave), solo para la prueba.</summary>
    private static (string Certificado, string Clave) CertificadoAutofirmado()
    {
        using var rsa = RSA.Create(2048);
        var solicitud = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var nombres = new SubjectAlternativeNameBuilder();
        nombres.AddDnsName("localhost");
        nombres.AddIpAddress(System.Net.IPAddress.Loopback);
        solicitud.CertificateExtensions.Add(nombres.Build());
        using var certificado = solicitud.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return (certificado.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }
}

/// <summary>La misma API con el broker en TLS (8883) y un certificado autofirmado: prueba <c>Mqtt__UsarTls</c>.</summary>
public sealed class FabricaVitalifyMqttTls : FabricaVitalifyMqtt
{
    protected override bool UsarTls => true;
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionApiMqttTls : ICollectionFixture<FabricaVitalifyMqttTls>
{
    public const string Nombre = "api-mqtt-tls";
}

public sealed record MensajeRecibido(string Topico, string Payload, bool Retenido);

/// <summary>Cliente MQTT de prueba suscrito a un tópico.</summary>
public sealed class SuscripcionPrueba(IMqttClient cliente) : IAsyncDisposable
{
    public System.Collections.Concurrent.ConcurrentQueue<MensajeRecibido> Mensajes { get; } = new();

    public async ValueTask DisposeAsync()
    {
        if (cliente.IsConnected)
        {
            await cliente.DisconnectAsync();
        }

        cliente.Dispose();
    }
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionApiMqtt : ICollectionFixture<FabricaVitalifyMqtt>
{
    public const string Nombre = "api-mqtt";
}
