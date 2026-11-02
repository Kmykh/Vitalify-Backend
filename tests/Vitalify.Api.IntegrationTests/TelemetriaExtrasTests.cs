using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.Contratos;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Telemetria;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>Reglas de la ingesta: duplicados, búfer offline, ventana de tiempo, dispositivos y permisos.</summary>
public class TelemetriaExtrasTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task UnaLecturaDuplicada_DevuelveDuplicadaYNoCreaOtraFila()
    {
        var (_, _, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        var mensaje = new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 7, fc = 80 };
        await administrador.RegistrarTelemetriaAsync(mensaje);

        var repetida = await administrador.RegistrarTelemetriaAsync(mensaje);

        Assert.Equal("Duplicada", repetida.Estado);
        Assert.Empty(repetida.Incidencias);
        Assert.Equal(1, await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.CountAsync(l => l.CodigoDispositivo == sensor.Codigo)));
    }

    [Fact]
    public async Task UnaLecturaAtrasadaDelBufer_SeGuardaPeroNoPisaElEstadoMasNuevo()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        var ahora = Ms(Fabrica.Reloj.AhoraUtc);
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(ahora), seq = 10, fc = 80 });

        var atrasada = await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(ahora.AddMinutes(-3)), seq = 9, fc = 120, temp = 37.4 });

        Assert.Equal("Aceptada", atrasada.Estado);
        Assert.Equal(2, await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.CountAsync(l => l.CodigoDispositivo == sensor.Codigo)));
        var signos = (await enfermera.GetFromJsonAsync<SignosActualesDto>($"/api/v1/pacientes/{ficha.Id}/signos/actual"))!;
        Assert.Equal((80m, ahora), (signos.Fc.Valor, signos.Fc.MedidoEn));
        Assert.Equal((37.4m, ahora.AddMinutes(-3)), (signos.Temperatura.Valor, signos.Temperatura.MedidoEn));
        Assert.Equal(ahora, signos.UltimaLecturaEn);
    }

    [Theory]
    [InlineData(5 * 60)]
    [InlineData(-25 * 3600)]
    public async Task UnaMarcaDeTiempoEnElFuturoOConMasDe24HorasSeRechazaConIncidencia(int segundos)
    {
        var (_, _, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();

        var resultado = await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc.AddSeconds(segundos)), seq = 1, fc = 80 });

        Assert.Equal("Rechazada", resultado.Estado);
        Assert.Equal("MarcaDeTiempoInvalida", Assert.Single(resultado.Incidencias).Tipo);
        Assert.Equal(0, await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.CountAsync(l => l.CodigoDispositivo == sensor.Codigo)));
    }

    [Fact]
    public async Task UnDispositivoSinVincularODesconocido_GeneraIncidenciaYNoGuardaNada()
    {
        var administrador = await ClienteAdministradorAsync();
        var libre = await administrador.CrearDispositivoAsync();
        var desconocido = $"NO-EXISTE-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        var sinVincular = await administrador.RegistrarTelemetriaAsync(new { dispositivo = libre.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 1, fc = 80 });
        var noRegistrado = await administrador.RegistrarTelemetriaAsync(new { dispositivo = desconocido, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 1, fc = 80 });

        Assert.Equal(("Rechazada", "DispositivoSinVincular"), (sinVincular.Estado, Assert.Single(sinVincular.Incidencias).Tipo));
        Assert.Equal(("Rechazada", "DispositivoDesconocido"), (noRegistrado.Estado, Assert.Single(noRegistrado.Incidencias).Tipo));
        Assert.Equal(0, await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.CountAsync(l => l.CodigoDispositivo == libre.Codigo || l.CodigoDispositivo == desconocido)));
    }

    [Fact]
    public async Task DespuesDelEgreso_LasLecturasDeEseDispositivoYaNoSeAsocianAlPaciente()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 1, fc = 80 });
        Assert.Equal(HttpStatusCode.OK, (await enfermera.EgresarAsync(ficha.Id)).StatusCode);

        var despues = await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc.AddSeconds(1)), seq = 2, fc = 81 });

        Assert.Equal(("Rechazada", "DispositivoSinVincular"), (despues.Estado, Assert.Single(despues.Incidencias).Tipo));
        Assert.Equal(1, await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.CountAsync(l => l.PacienteId == ficha.Id)));
    }

    [Theory]
    [InlineData(89, "vigente")]
    [InlineData(91, "pendiente-actualizacion")]
    public async Task ElEstadoPendienteSeCalculaAlLeerConElRelojAjustable(int segundos, string esperado)
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        await (await ClienteAdministradorAsync()).RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 1, fc = 80 });

        SignosActualesDto signos;
        using (Fabrica.Reloj.Adelantar(TimeSpan.FromSeconds(segundos)))
        {
            signos = (await enfermera.GetFromJsonAsync<SignosActualesDto>($"/api/v1/pacientes/{ficha.Id}/signos/actual"))!;
        }

        Assert.Equal(esperado, signos.Fc.Estado);
        Assert.Equal(80m, signos.Fc.Valor);
    }

    [Fact]
    public async Task MonitoreadosIndicaLaUltimaLecturaYLaSenal()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var medidoEn = Ms(Fabrica.Reloj.AhoraUtc);
        await (await ClienteAdministradorAsync()).RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(medidoEn), seq = 1, fc = 80 });

        var conDatos = await ItemAsync(enfermera, ficha.Id);
        PacienteMonitoreadoItem sinSenal;
        using (Fabrica.Reloj.Adelantar(TimeSpan.FromSeconds(181)))
        {
            sinSenal = await ItemAsync(enfermera, ficha.Id);
        }

        Assert.Equal(("con-datos", medidoEn), (conDatos.Senal, conDatos.UltimaLecturaEn));
        Assert.Equal("sin-senal", sinSenal.Senal);
    }

    [Fact]
    public async Task UnMensajeSinLosCamposObligatoriosDevuelve400PorCampo()
    {
        var respuesta = await (await ClienteAdministradorAsync()).EnviarTelemetriaAsync(new { fc = 80 });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("\"dispositivo\"", cuerpo);
        Assert.Contains("\"ts\"", cuerpo);
        Assert.Contains("\"seq\"", cuerpo);
    }

    [Fact]
    public async Task LaEnfermeraRecibe403EnIncidenciasYElAdministrador403EnLosSignos()
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await enfermera.GetAsync("/api/v1/telemetria/incidencias")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync($"/api/v1/pacientes/{ficha.Id}/signos/actual")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await enfermera.EnviarTelemetriaAsync(new { dispositivo = "ESP32-001", ts = Ts(DateTime.UtcNow), seq = 1 })).StatusCode);
    }

    private static async Task<PacienteMonitoreadoItem> ItemAsync(HttpClient cliente, Guid pacienteId)
    {
        var lista = (await cliente.GetFromJsonAsync<Lista>("/api/v1/pacientes/monitoreados"))!;
        return lista.Items.Single(i => i.PacienteId == pacienteId);
    }

    private sealed record Lista(IReadOnlyList<PacienteMonitoreadoItem> Items);

    private sealed record PacienteMonitoreadoItem(Guid PacienteId, DateTime? UltimaLecturaEn, string Senal);
}

/// <summary>El endpoint de desarrollo no existe fuera de Development.</summary>
[Collection(ColeccionApiProduccion.Nombre)]
public class TelemetriaEnProduccionTests(FabricaVitalifyProduccion fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task ElEndpointDeTelemetriaDeDesarrolloNoExisteFueraDeDevelopment()
    {
        var administrador = await ClienteAdministradorAsync();

        var respuesta = await administrador.EnviarTelemetriaAsync(new { dispositivo = "ESP32-001", ts = Ts(DateTime.UtcNow), seq = 1, fc = 80 });

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
}
