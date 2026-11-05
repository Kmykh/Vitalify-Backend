using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Historial;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>
/// Retención del historial (fase 7, parte B) con el SQL real: las lecturas crudas de un episodio egresado hace más de
/// 30 días pasan a <c>lectura_signos_resumen</c> (5 min) y se borran. Base propia: la retención resume todo lo que
/// cumpla la regla, también lo de otras pruebas.
/// </summary>
[Collection(ColeccionApiRetencion.Nombre)]
public class Fase7RetencionTests(FabricaVitalifyRetencion fabrica) : PruebaApi(fabrica)
{
    private long _seq;

    /// <summary>
    /// Una lectura cada 10 s durante <paramref name="cantidad"/> × 10 s, desde 2 min después de un múltiplo de 5 min de
    /// hace 5 h: los grupos de 5 min quedan incompletos en los bordes. Los valores varían, la FR falta en una de cada
    /// tres y hay una caída.
    /// </summary>
    private async Task EnviarLecturasAsync(HttpClient administrador, string sensor, int cantidad)
    {
        var haceCincoHoras = Fabrica.Reloj.AhoraUtc.AddHours(-5);
        var inicio = new DateTime(haceCincoHoras.Ticks - (haceCincoHoras.Ticks % TimeSpan.FromMinutes(5).Ticks), DateTimeKind.Utc).AddMinutes(2);
        for (var i = 0; i < cantidad; i++)
        {
            var (ts, seq, fc, spo2, temp) = (Ts(inicio.AddSeconds(10 * i)), Interlocked.Increment(ref _seq), 70 + (i * 7 % 31), 92 + (i % 7), 36.0m + (i % 10 / 10m));
            object lectura = i % 3 == 0
                ? new { dispositivo = sensor, ts, seq, fc, spo2, temp, caida = i == 40 }
                : new { dispositivo = sensor, ts, seq, fc, spo2, temp, fr = 14 + (i % 9), caida = i == 40 };
            await administrador.RegistrarTelemetriaAsync(lectura);
        }
    }

    private Task<int> CrudasAsync(Guid pacienteId) =>
        Fabrica.ConsultarHistorialAsync(db => db.Lecturas.CountAsync(l => l.PacienteId == pacienteId));

    private Task<List<int>> ResumenesAsync(Guid pacienteId) =>
        Fabrica.ConsultarHistorialAsync(db => db.ResumenesLecturas.Where(r => r.PacienteId == pacienteId).Select(r => r.Lecturas).ToListAsync());

    private async Task<ResultadoRetencion> ResumirDentroDeAsync(TimeSpan despues)
    {
        using (Fabrica.Reloj.Adelantar(despues))
        {
            return await Fabrica.ResumirHistorialAsync();
        }
    }

    [Fact]
    public async Task Retencion_NoTocaHospitalizacionesActivasNiEpisodiosEgresadosHaceMenosDe30Dias()
    {
        var administrador = await ClienteAdministradorAsync();
        var (enfermeraA, egresado, sensorA, _) = await PacienteConSensorAsync();
        var (_, activo, sensorB, _) = await PacienteConSensorAsync();
        await EnviarLecturasAsync(administrador, sensorA.Codigo, 12);
        await EnviarLecturasAsync(administrador, sensorB.Codigo, 12);
        Assert.Equal(HttpStatusCode.OK, (await enfermeraA.EgresarAsync(egresado.Id)).StatusCode);

        await ResumirDentroDeAsync(TimeSpan.FromDays(29));
        Assert.Equal((12, 12), (await CrudasAsync(egresado.Id), await CrudasAsync(activo.Id)));
        Assert.Empty(await ResumenesAsync(egresado.Id));

        var resultado = await ResumirDentroDeAsync(TimeSpan.FromDays(31));
        Assert.True(resultado.Hospitalizaciones >= 1);
        Assert.Equal(0, await CrudasAsync(egresado.Id));
        Assert.Equal(12, (await ResumenesAsync(egresado.Id)).Sum());
        Assert.Equal(12, await CrudasAsync(activo.Id)); // activa: aunque sus lecturas tengan más de 30 días, no se toca
        Assert.Empty(await ResumenesAsync(activo.Id));
    }

    [Fact]
    public async Task Retencion_ElResumenCoincideConLasLecturasCrudas_YElHistorialAgrupadoLoLee()
    {
        var administrador = await ClienteAdministradorAsync();
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        await EnviarLecturasAsync(administrador, sensor.Codigo, 60);
        Assert.Equal(HttpStatusCode.OK, (await enfermera.EgresarAsync(ficha.Id)).StatusCode);
        var medico = await ClienteMedicoAsync();
        string[] intervalos = ["5m", "15m", "1h"];

        async Task<Dictionary<string, HistorialSignosDto>> AgrupadosAsync()
        {
            var porIntervalo = new Dictionary<string, HistorialSignosDto>();
            foreach (var agrupar in intervalos)
            {
                porIntervalo[agrupar] = (await medico.GetFromJsonAsync<HistorialSignosDto>($"/api/v1/pacientes/{ficha.Id}/signos/historial?agrupar={agrupar}"))!;
            }

            return porIntervalo;
        }

        var antes = await AgrupadosAsync();
        var extremosAntes = (await medico.GetFromJsonAsync<ResumenPacienteDto>($"/api/v1/pacientes/{ficha.Id}/resumen"))!.Extremos.Sensor;

        await ResumirDentroDeAsync(TimeSpan.FromDays(31));

        var despues = await AgrupadosAsync();
        var extremosDespues = (await medico.GetFromJsonAsync<ResumenPacienteDto>($"/api/v1/pacientes/{ficha.Id}/resumen"))!.Extremos.Sensor;
        var crudo = (await medico.GetFromJsonAsync<HistorialSignosDto>($"/api/v1/pacientes/{ficha.Id}/signos/historial"))!;
        var porMinuto = (await medico.GetFromJsonAsync<HistorialSignosDto>($"/api/v1/pacientes/{ficha.Id}/signos/historial?agrupar=1m"))!;

        // Las crudas ya no existen: todo lo agrupado sale del resumen y coincide con lo que daban las crudas
        // (promedio ponderado, mínimo, máximo, cantidad de lecturas y caída), en 5 min, 15 min y 1 h.
        Assert.Equal(0, await CrudasAsync(ficha.Id));
        Assert.Equal(3, (await ResumenesAsync(ficha.Id)).Count);
        foreach (var agrupar in intervalos)
        {
            Assert.All(antes[agrupar].Grupos!, g => Assert.False(g.Resumido));
            Assert.All(despues[agrupar].Grupos!, g => Assert.True(g.Resumido));
            Assert.Equal(antes[agrupar].Grupos!, despues[agrupar].Grupos!.Select(g => g with { Resumido = false }));
            Assert.Null(antes[agrupar].ResumidoHasta);
            Assert.NotNull(despues[agrupar].ResumidoHasta);
        }

        Assert.Equal(60, despues["5m"].Grupos!.Sum(g => g.Lecturas));
        Assert.Contains(despues["5m"].Grupos!, g => g.Caida);
        Assert.Equal(extremosAntes, extremosDespues);

        // Sin agrupar no hay crudas que mostrar, y la respuesta dice desde cuándo; con 1m, los grupos resumidos duran 5 min.
        Assert.Empty(crudo.Lecturas);
        Assert.Equal(despues["5m"].Grupos![^1].Fin, crudo.ResumidoHasta);
        Assert.Equal(despues["5m"].Grupos!.Select(g => (g.Inicio, g.Fin)), porMinuto.Grupos!.Select(g => (g.Inicio, g.Fin)));
    }
}
