using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.Contratos;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Telemetria;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU10 · Captura automática de frecuencia cardíaca y respiratoria.</summary>
public class HU10CapturaFcFrTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU10_E1_SensorVinculadoEnviaFcYFr_SeGuardaConSuMarcaDeTiempoAsociadaAlPaciente()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        var medidoEn = Ms(Fabrica.Reloj.AhoraUtc.AddSeconds(-3));

        var resultado = await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(medidoEn), seq = 1, fc = 82, fr = 16 });

        Assert.Equal("Aceptada", resultado.Estado);
        var lectura = await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.SingleAsync(l => l.CodigoDispositivo == sensor.Codigo));
        Assert.Equal((ficha.HospitalizacionActiva!.Id, ficha.Id), (lectura.HospitalizacionId, lectura.PacienteId));
        Assert.Equal((medidoEn, 82, 16, OrigenLectura.ApiDesarrollo), (lectura.MedidoEn, lectura.Fc, lectura.Fr, lectura.Origen));

        var signos = (await enfermera.GetFromJsonAsync<SignosActualesDto>($"/api/v1/pacientes/{ficha.Id}/signos/actual"))!;
        Assert.Equal(new VariableSignoDto(82, medidoEn, "vigente"), signos.Fc);
        Assert.Equal(new VariableSignoDto(16, medidoEn, "vigente"), signos.Fr);
    }

    [Fact]
    public async Task HU10_E2_ValorFueraDelRangoFisiologico_SeDescartaComoErrorDeSensorYQuedaEnElLogDeIncidencias()
    {
        var (_, _, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();

        var resultado = await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 1, fc = 400, fr = 16 });

        Assert.Equal("AceptadaParcial", resultado.Estado);
        var lectura = await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.SingleAsync(l => l.CodigoDispositivo == sensor.Codigo));
        Assert.Equal((null, 16), (lectura.Fc, lectura.Fr));

        var incidencias = (await administrador.GetFromJsonAsync<PaginaRespuesta<IncidenciaDto>>($"/api/v1/telemetria/incidencias?dispositivo={sensor.Codigo}"))!;
        var incidencia = Assert.Single(incidencias.Elementos);
        Assert.Equal(("FueraDeRango", "Fc", 400m), (incidencia.Tipo, incidencia.Variable, incidencia.ValorRecibido));
    }
}

/// <summary>HU11 · Captura automática de temperatura y SpO2.</summary>
public class HU11CapturaTemperaturaYSpo2Tests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU11_E1_LlegaTemperaturaYSpo2_SeActualizanAmbosEnElEstadoActual()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var medidoEn = Ms(Fabrica.Reloj.AhoraUtc);

        await (await ClienteAdministradorAsync()).RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(medidoEn), seq = 1, temp = 36.9, spo2 = 97 });

        var signos = (await enfermera.GetFromJsonAsync<SignosActualesDto>($"/api/v1/pacientes/{ficha.Id}/signos/actual"))!;
        Assert.Equal(new VariableSignoDto(36.9m, medidoEn, "vigente"), signos.Temperatura);
        Assert.Equal(new VariableSignoDto(97, medidoEn, "vigente"), signos.Spo2);
        Assert.Equal((medidoEn, "con-datos"), (signos.UltimaLecturaEn, signos.Senal));
    }

    [Fact]
    public async Task HU11_E2_ElSensorDeTemperaturaDejaDeEnviar_SeMantieneElUltimoValorMarcadoComoPendiente()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        var inicio = Ms(Fabrica.Reloj.AhoraUtc);
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(inicio), seq = 1, temp = 37.1, spo2 = 97 });

        SignosActualesDto signos;
        using (Fabrica.Reloj.Adelantar(TimeSpan.FromSeconds(100)))
        {
            // El sensor sigue enviando SpO2, pero ya no temperatura.
            await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 2, spo2 = 96 });
            signos = (await enfermera.GetFromJsonAsync<SignosActualesDto>($"/api/v1/pacientes/{ficha.Id}/signos/actual"))!;
        }

        Assert.Equal(new VariableSignoDto(37.1m, inicio, "pendiente-actualizacion"), signos.Temperatura);
        Assert.Equal((96m, "vigente"), (signos.Spo2.Valor, signos.Spo2.Estado));
    }
}

/// <summary>HU12 · Captura automática de presión arterial.</summary>
public class HU12CapturaPresionTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU12_E1_LlegaSistolicaYDiastolica_SeGuardanAmbasConLaMarcaDeTiempoDeLaMedicion()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var medidoEn = Ms(Fabrica.Reloj.AhoraUtc.AddMinutes(-1));

        var resultado = await (await ClienteAdministradorAsync()).RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(medidoEn), seq = 1, pas = 118, pad = 76 });

        Assert.Equal("Aceptada", resultado.Estado);
        var lectura = await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.SingleAsync(l => l.CodigoDispositivo == sensor.Codigo));
        Assert.Equal((118, 76, medidoEn), (lectura.Pas, lectura.Pad, lectura.MedidoEn));
        var signos = (await enfermera.GetFromJsonAsync<SignosActualesDto>($"/api/v1/pacientes/{ficha.Id}/signos/actual"))!;
        Assert.Equal((118m, 76m, medidoEn, medidoEn), (signos.Pas.Valor, signos.Pad.Valor, signos.Pas.MedidoEn, signos.Pad.MedidoEn));
    }

    [Fact]
    public async Task HU12_E2_LlegaLaSistolicaSinLaDiastolica_PresionInvalidaConIncidenciaYSolicitudDeNuevaLectura()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();

        var resultado = await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 1, fc = 80, pas = 118 });

        Assert.Equal("AceptadaParcial", resultado.Estado);
        var incidencia = Assert.Single(resultado.Incidencias);
        Assert.Equal(("PresionIncompleta", true), (incidencia.Tipo, incidencia.RequiereNuevaLectura));
        Assert.Contains(Fabrica.SolicitudesNuevaLectura.Todas, s => s.CodigoDispositivo == sensor.Codigo);

        var signos = (await enfermera.GetFromJsonAsync<SignosActualesDto>($"/api/v1/pacientes/{ficha.Id}/signos/actual"))!;
        Assert.Equal(("sin-datos", "sin-datos", "vigente"), (signos.Pas.Estado, signos.Pad.Estado, signos.Fc.Estado));
        var guardada = (await administrador.GetFromJsonAsync<PaginaRespuesta<IncidenciaDto>>(
            $"/api/v1/telemetria/incidencias?dispositivo={sensor.Codigo}&tipo=PresionIncompleta"))!;
        Assert.True(Assert.Single(guardada.Elementos).RequiereNuevaLectura);
    }
}
