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
public sealed class FabricaVitalifyMqtt : FabricaVitalify
{
    private const string ConfiguracionMosquitto = "listener 1883\nallow_anonymous true\npersistence false\n";

    private readonly IContainer _mosquitto = new ContainerBuilder("eclipse-mosquitto:2")
        .WithResourceMapping(Encoding.UTF8.GetBytes(ConfiguracionMosquitto), "/mosquitto/config/mosquitto.conf")
        .WithPortBinding(1883, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("mosquitto version .* running"))
        .Build();

    public string HostBroker => _mosquitto.Hostname;

    public int PuertoBroker => _mosquitto.GetMappedPublicPort(1883);

    /// <summary>Publica con QoS 1, como el firmware.</summary>
    public async Task PublicarAsync(string topico, string payload, bool retenido = false)
    {
        using var cliente = new MqttFactory().CreateMqttClient();
        await cliente.ConnectAsync(new MqttClientOptionsBuilder()
            .WithTcpServer(HostBroker, PuertoBroker)
            .WithClientId($"prueba-{Guid.NewGuid():N}")
            .Build());
        await cliente.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topico)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retenido)
            .Build());
        await cliente.DisconnectAsync();
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
        await _mosquitto.StartAsync();
        return new Dictionary<string, string>
        {
            ["Mqtt__Habilitado"] = "true",
            ["Mqtt__Servidor"] = HostBroker,
            ["Mqtt__Puerto"] = PuertoBroker.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Mqtt__Usuario"] = "prueba",
            ["Mqtt__Clave"] = "prueba", // el broker de prueba acepta anónimos; nunca debe colarse la clave real del .env
            ["Mqtt__ClientId"] = $"vitalify-backend-pruebas-{Guid.NewGuid():N}",
        };
    }

    protected override async Task LiberarAdicionalAsync() => await _mosquitto.DisposeAsync();
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionApiMqtt : ICollectionFixture<FabricaVitalifyMqtt>
{
    public const string Nombre = "api-mqtt";
}
