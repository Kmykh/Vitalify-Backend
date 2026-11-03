using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Domain.Telemetria;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>
/// El receptor con <c>Mqtt__UsarTls=true</c> contra un Mosquitto con TLS (8883) y un certificado autofirmado. Es la única
/// prueba con <c>Mqtt__ValidarCertificado=false</c>, que solo se acepta en Development.
/// </summary>
[Collection(ColeccionApiMqttTls.Nombre)]
public class MqttConTlsTests(FabricaVitalifyMqttTls fabrica) : PruebaApi(fabrica)
{
    private FabricaVitalifyMqtt Mqtt => (FabricaVitalifyMqtt)Fabrica;

    [Fact]
    public async Task ConTlsLaLecturaDelWearableLlegaAlHistorial_YHealthMuestraElBrokerSinLaClave()
    {
        var (_, ficha, sensor, _) = await PacienteConSensorAsync();
        var medidoEn = Ms(Fabrica.Reloj.AhoraUtc);
        var cliente = Fabrica.CrearCliente();
        await FabricaVitalifyMqtt.EsperarAsync(async () => (await MqttEnHealthAsync(cliente)).Estado == "Healthy", "el receptor se conecte por TLS");

        await Mqtt.PublicarAsync(
            $"device/{sensor.Codigo}/telemetria",
            $$"""{"dispositivo":"{{sensor.Codigo}}","ts":"{{Ts(medidoEn)}}","seq":1,"fc":84,"spo2":97,"temp":36.6,"caida":false}""");

        await FabricaVitalifyMqtt.EsperarAsync(
            () => Fabrica.ConsultarHistorialAsync(db => db.Lecturas.AnyAsync(l => l.CodigoDispositivo == sensor.Codigo)),
            "la lectura publicada por MQTT con TLS llegue al historial");
        var lectura = await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.SingleAsync(l => l.CodigoDispositivo == sensor.Codigo));
        Assert.Equal((OrigenLectura.Mqtt, ficha.Id, 84), (lectura.Origen, lectura.PacienteId, lectura.Fc));

        var (estado, descripcion, json) = await MqttEnHealthAsync(cliente);
        Assert.Equal("Healthy", estado);
        Assert.Equal($"Conectado a {Mqtt.HostBroker}:{Mqtt.PuertoBroker} (TLS).", descripcion);
        Assert.DoesNotContain("prueba", json, StringComparison.Ordinal); // ni el usuario ni la clave del broker
    }

    private static async Task<(string Estado, string? Descripcion, string Json)> MqttEnHealthAsync(HttpClient cliente)
    {
        var json = await cliente.GetStringAsync("/health");
        using var documento = JsonDocument.Parse(json);
        if (!documento.RootElement.GetProperty("checks").TryGetProperty("mqtt", out var mqtt))
        {
            return ("ausente", null, json);
        }

        return (mqtt.GetProperty("estado").GetString()!, mqtt.GetProperty("descripcion").GetString(), json);
    }
}
