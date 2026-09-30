using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vitalify.Api.Contratos;
using Vitalify.Api.IntegrationTests.Infraestructura;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU07 · Lista de pacientes monitoreados (base).</summary>
public class HU07PacientesMonitoreadosTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU07_E1_ElListadoMuestraNombreCamaUltimoPuntajeYNivelDeRiesgo()
    {
        var administrador = await ClienteAdministradorAsync();
        var (cama1, cama2) = (await administrador.CrearCamaAsync(), await administrador.CrearCamaAsync());
        var sensor = await administrador.CrearDispositivoAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var monitoreado = await enfermera.IngresarPacienteAsync(cama1.Id);
        var sinSensor = await enfermera.IngresarPacienteAsync(cama2.Id);
        await enfermera.VincularAsync(monitoreado.Id, sensor.Id);
        var medico = await ClienteMedicoAsync();

        var respuesta = await medico.GetAsync("/api/v1/pacientes/monitoreados");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;
        var item = json.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("pacienteId").GetGuid() == monitoreado.Id);
        Assert.Equal(monitoreado.NombreCompleto, item.GetProperty("nombreCompleto").GetString());
        Assert.Equal(monitoreado.Edad, item.GetProperty("edad").GetInt32());
        Assert.Equal(cama1.Codigo, item.GetProperty("cama").GetString());
        Assert.Equal("Medicina B", item.GetProperty("servicio").GetString());
        Assert.Equal(sensor.Codigo, item.GetProperty("codigoDispositivo").GetString());
        Assert.NotEqual(default, item.GetProperty("ingresoEn").GetDateTime());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("ultimoNews2").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("ultimoMews").ValueKind);
        Assert.Equal("sin-datos", item.GetProperty("nivelRiesgo").GetString());

        Assert.DoesNotContain(json.GetProperty("items").EnumerateArray(), i => i.GetProperty("pacienteId").GetGuid() == sinSensor.Id);
        Assert.False(json.TryGetProperty("mensaje", out _));
    }
}

/// <summary>HU07 E2 necesita una base sin pacientes monitoreados: usa su propio contenedor.</summary>
[Collection(ColeccionApiAislada.Nombre)]
public class HU07ListaVaciaTests(FabricaVitalifyAislada fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task HU07_E2_SinPacientesConSensorVinculado_Devuelve200ConListaVaciaYMensaje()
    {
        // Un paciente hospitalizado pero sin sensor no cuenta como monitoreado.
        var cama = await (await ClienteAdministradorAsync()).CrearCamaAsync();
        await (await ClienteEnfermeraAsync()).IngresarPacienteAsync(cama.Id);
        var medico = await ClienteMedicoAsync();

        var respuesta = await medico.GetAsync("/api/v1/pacientes/monitoreados");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cuerpo = (await respuesta.Content.ReadFromJsonAsync<PacientesMonitoreadosRespuesta>())!;
        Assert.Empty(cuerpo.Items);
        Assert.Equal("No hay pacientes con un sensor vinculado en este momento.", cuerpo.Mensaje);
    }
}
