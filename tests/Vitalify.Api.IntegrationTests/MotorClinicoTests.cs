using System.Net;
using System.Net.Http.Json;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Clinica;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>Motor clínico (fase 4): NEWS2 y MEWS con los datos del wearable y las observaciones de enfermería.</summary>
public class MotorClinicoTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task ConSoloLosDatosDelWearableElPuntajeEsParcialYLaObservacionLoCompleta()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        await (await ClienteAdministradorAsync()).RegistrarTelemetriaAsync(
            new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 1, fc = 112, spo2 = 93, temp = 38.3 });

        var parcial = (await enfermera.GetFromJsonAsync<RiesgoPacienteDto>($"/api/v1/pacientes/{ficha.Id}/riesgo"))!.UltimaEvaluacion!;
        Assert.Equal((5, "medio", false, "Lectura"), (parcial.News2.Total, parcial.News2.Nivel, parcial.News2.Completo, parcial.Origen));

        var respuesta = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/observaciones",
            new { fr = 22, pas = 105, pad = 70, conciencia = "Alerta", oxigenoSuplementario = false });

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.EndsWith($"/api/v1/pacientes/{ficha.Id}/riesgo", respuesta.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
        var completo = (await respuesta.Content.ReadFromJsonAsync<ObservacionRegistradaDto>())!.Riesgo;
        Assert.Equal((8, "alto", true), (completo.News2.Total, completo.News2.Nivel, completo.News2.Completo));
        // MEWS: FC 112 → 2, FR 22 → 2, PAS 105 → 0, T 38,3 → 0, alerta → 0 (no usa SpO2).
        Assert.Equal((4, "medio", true), (completo.Mews.Total, completo.Mews.Nivel, completo.Mews.Completo));

        var monitoreados = await enfermera.GetStringAsync("/api/v1/pacientes/monitoreados");
        Assert.Contains("\"ultimoNews2\":8", monitoreados);
        Assert.Contains("\"nivelRiesgo\":\"alto\"", monitoreados);
        Assert.Contains("\"evaluacionCompleta\":true", monitoreados);
    }

    [Fact]
    public async Task UnaObservacionInvalidaDevuelve400()
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();

        var respuesta = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/observaciones", new { pas = 120 });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Contains("\"pas\"", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ElAdministradorNoVeElRiesgoNiRegistraObservacionesYElMedicoSi()
    {
        var (_, ficha, _, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        var medico = await ClienteMedicoAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync($"/api/v1/pacientes/{ficha.Id}/riesgo")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/observaciones", new { fr = 16 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await medico.GetAsync($"/api/v1/pacientes/{ficha.Id}/riesgo")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await medico.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/observaciones", new { conciencia = "Alerta" })).StatusCode);
    }
}
