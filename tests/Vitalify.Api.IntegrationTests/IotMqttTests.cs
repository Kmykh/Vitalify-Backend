using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Clinica;
using Vitalify.Domain.Telemetria;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>
/// El backend y el wearable hablan lo mismo: se publica en un Mosquitto real exactamente lo que publica el firmware
/// (tópico, QoS 1 y JSON de construirJsonTelemetria) y el backend lo registra, evalúa el riesgo y sigue la presencia.
/// </summary>
[Collection(ColeccionApiMqtt.Nombre)]
public class IotMqttTests(FabricaVitalifyMqtt fabrica) : PruebaApi(fabrica)
{
    private FabricaVitalifyMqtt Mqtt => (FabricaVitalifyMqtt)Fabrica;

    /// <summary>Mismo formato que construirJsonTelemetria del firmware (pruebas.cpp).</summary>
    private static string JsonFirmware(string dispositivo, DateTime ts, int seq, int fc, int spo2, string temp) =>
        $$"""{"dispositivo":"{{dispositivo}}","ts":"{{Ts(ts)}}","seq":{{seq}},"fc":{{fc}},"spo2":{{spo2}},"temp":{{temp}},"caida":false}""";

    [Fact]
    public async Task UnaLecturaPublicadaPorElWearableSeRegistraYEvaluaElRiesgo()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var medidoEn = Ms(Fabrica.Reloj.AhoraUtc);

        await Mqtt.PublicarAsync($"device/{sensor.Codigo}/telemetria", JsonFirmware(sensor.Codigo, medidoEn, 0, 112, 93, "38.3"));

        await FabricaVitalifyMqtt.EsperarAsync(
            () => Fabrica.ConsultarHistorialAsync(db => db.Lecturas.AnyAsync(l => l.CodigoDispositivo == sensor.Codigo)),
            "la lectura publicada por MQTT llegue al historial");
        var lectura = await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.SingleAsync(l => l.CodigoDispositivo == sensor.Codigo));
        Assert.Equal((OrigenLectura.Mqtt, medidoEn, ficha.Id, 112, 93, 38.3m),
            (lectura.Origen, lectura.MedidoEn, lectura.PacienteId, lectura.Fc, lectura.Spo2, lectura.Temperatura));

        await FabricaVitalifyMqtt.EsperarAsync(
            () => Fabrica.ConsultarHistorialAsync(db => db.Evaluaciones.AnyAsync(e => e.PacienteId == ficha.Id)),
            "el motor clínico evalúe la lectura");
        var riesgo = (await enfermera.GetFromJsonAsync<RiesgoPacienteDto>($"/api/v1/pacientes/{ficha.Id}/riesgo"))!.UltimaEvaluacion!;
        Assert.Equal((5, "medio", false), (riesgo.News2.Total, riesgo.News2.Nivel, riesgo.News2.Completo));
        Assert.Equal(["fr", "oxigenoSuplementario", "pas", "conciencia"], riesgo.News2.Faltantes);
    }

    [Fact]
    public async Task ElEstadoOnlineYOfflineDelWearableSeVeEnMonitoreados()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();

        await Mqtt.PublicarAsync($"device/{sensor.Codigo}/estado", "online", retenido: true);
        await FabricaVitalifyMqtt.EsperarAsync(async () => await ConexionAsync(enfermera, ficha.Id) == "en-linea", "el wearable figure en línea");

        // El broker publica "offline" (last will) cuando el ESP32 desaparece.
        await Mqtt.PublicarAsync($"device/{sensor.Codigo}/estado", "offline", retenido: true);
        await FabricaVitalifyMqtt.EsperarAsync(async () => await ConexionAsync(enfermera, ficha.Id) == "fuera-de-linea", "el wearable figure fuera de línea");
    }

    [Fact]
    public async Task UnMensajeConOtroCodigoQueElDelTopicoOMalformadoSeIgnoraSinDetenerElReceptor()
    {
        var (_, _, sensor, _) = await PacienteConSensorAsync();
        var (_, _, otro, _) = await PacienteConSensorAsync();

        await Mqtt.PublicarAsync($"device/{otro.Codigo}/telemetria", JsonFirmware(sensor.Codigo, Fabrica.Reloj.AhoraUtc, 1, 80, 97, "36.8"));
        await Mqtt.PublicarAsync($"device/{sensor.Codigo}/telemetria", "{no es json");
        await Mqtt.PublicarAsync($"device/{sensor.Codigo}/telemetria", JsonFirmware(sensor.Codigo, Fabrica.Reloj.AhoraUtc.AddSeconds(1), 2, 81, 97, "36.8"));

        await FabricaVitalifyMqtt.EsperarAsync(
            () => Fabrica.ConsultarHistorialAsync(db => db.Lecturas.AnyAsync(l => l.CodigoDispositivo == sensor.Codigo)),
            "la lectura válida posterior se registre");
        Assert.Equal(1, await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.CountAsync(l => l.CodigoDispositivo == sensor.Codigo)));
        Assert.Equal(0, await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.CountAsync(l => l.CodigoDispositivo == otro.Codigo)));
    }

    [Fact]
    public async Task UnReenvioDelMismoMensajeQoS1NoDuplicaLaLectura()
    {
        var (_, _, sensor, _) = await PacienteConSensorAsync();
        var mensaje = JsonFirmware(sensor.Codigo, Ms(Fabrica.Reloj.AhoraUtc), 7, 80, 97, "36.8");

        await Mqtt.PublicarAsync($"device/{sensor.Codigo}/telemetria", mensaje);
        await Mqtt.PublicarAsync($"device/{sensor.Codigo}/telemetria", mensaje);
        await Mqtt.PublicarAsync($"device/{sensor.Codigo}/telemetria", JsonFirmware(sensor.Codigo, Ms(Fabrica.Reloj.AhoraUtc).AddSeconds(10), 8, 80, 97, "36.8"));

        await FabricaVitalifyMqtt.EsperarAsync(
            () => Fabrica.ConsultarHistorialAsync(db => db.Lecturas.AnyAsync(l => l.CodigoDispositivo == sensor.Codigo && l.Seq == 8)),
            "llegue la lectura siguiente");
        Assert.Equal(2, await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.CountAsync(l => l.CodigoDispositivo == sensor.Codigo)));
    }

    [Fact]
    public async Task HealthIncluyeLaConexionConElBroker()
    {
        await FabricaVitalifyMqtt.EsperarAsync(async () =>
        {
            var cuerpo = await Fabrica.CrearCliente().GetStringAsync("/health");
            return cuerpo.Contains("\"mqtt\"") && !cuerpo.Contains("Unhealthy");
        }, "el chequeo mqtt esté en verde");
    }

    private static async Task<string> ConexionAsync(HttpClient cliente, Guid pacienteId)
    {
        var respuesta = await cliente.GetFromJsonAsync<Monitoreados>("/api/v1/pacientes/monitoreados");
        return respuesta!.Items.Single(i => i.PacienteId == pacienteId).ConexionSensor;
    }

    private sealed record Monitoreados(IReadOnlyList<Item> Items);

    private sealed record Item(Guid PacienteId, string ConexionSensor);
}
