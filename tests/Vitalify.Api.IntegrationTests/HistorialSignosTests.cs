using System.Net;
using System.Net.Http.Json;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Telemetria;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>Historial de lecturas para los gráficos del dashboard (<c>GET /pacientes/{id}/signos/historial</c>).</summary>
public class HistorialSignosTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task SinParametros_DevuelveLasLecturasDeLaUltimaHoraEnOrdenCronologico()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        var ahora = Ms(Fabrica.Reloj.AhoraUtc);
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(ahora.AddMinutes(-90)), seq = 1, fc = 70 });
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(ahora.AddSeconds(-10)), seq = 3, fc = 82, spo2 = 97, temp = 36.8 });
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(ahora.AddMinutes(-20)), seq = 2, fc = 78, caida = true });

        var historial = (await enfermera.GetFromJsonAsync<HistorialSignosDto>($"/api/v1/pacientes/{ficha.Id}/signos/historial"))!;

        Assert.Equal([ahora.AddMinutes(-20), ahora.AddSeconds(-10)], historial.Lecturas.Select(l => l.MedidoEn));
        Assert.Equal((78, true), (historial.Lecturas[0].Fc, historial.Lecturas[0].Caida));
        Assert.Equal((82, 97, 36.8m), (historial.Lecturas[1].Fc, historial.Lecturas[1].Spo2, historial.Lecturas[1].Temperatura));
        Assert.Null(historial.Lecturas[1].Fr);
    }

    [Fact]
    public async Task ConDesdeYHasta_FiltraPorElRangoYElMedicoTambienLoVe()
    {
        var (_, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        var ahora = Ms(Fabrica.Reloj.AhoraUtc);
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(ahora.AddHours(-5)), seq = 1, fc = 70 });
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(ahora.AddHours(-3)), seq = 2, fc = 75 });
        var medico = await ClienteMedicoAsync();

        var historial = (await medico.GetFromJsonAsync<HistorialSignosDto>(
            $"/api/v1/pacientes/{ficha.Id}/signos/historial?desde={Ts(ahora.AddHours(-6))}&hasta={Ts(ahora.AddHours(-4))}"))!;

        Assert.Equal(70, Assert.Single(historial.Lecturas).Fc);
    }

    [Theory]
    [InlineData(-1, -2)]
    [InlineData(-25, 0)]
    public async Task UnRangoInvertidoODeMasDe24Horas_Devuelve400(int horasDesde, int horasHasta)
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();
        var ahora = Fabrica.Reloj.AhoraUtc;

        var respuesta = await enfermera.GetAsync(
            $"/api/v1/pacientes/{ficha.Id}/signos/historial?desde={Ts(ahora.AddHours(horasDesde))}&hasta={Ts(ahora.AddHours(horasHasta))}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task ElAdministradorRecibe403YUnPacienteInexistente404()
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync($"/api/v1/pacientes/{ficha.Id}/signos/historial")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await enfermera.GetAsync($"/api/v1/pacientes/{Guid.NewGuid()}/signos/historial")).StatusCode);
    }
}
