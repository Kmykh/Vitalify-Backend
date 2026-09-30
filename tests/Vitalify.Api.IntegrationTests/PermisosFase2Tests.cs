using System.Net;
using System.Net.Http.Json;
using Vitalify.Api.IntegrationTests.Infraestructura;

namespace Vitalify.Api.IntegrationTests;

/// <summary>Matriz de permisos de la fase 2 (docs/matriz-permisos.md).</summary>
public class PermisosFase2Tests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task ElAdministradorRecibe403EnMonitoreadosYEnElIngresoDePacientes()
    {
        var administrador = await ClienteAdministradorAsync();
        var cama = await administrador.CrearCamaAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync("/api/v1/pacientes/monitoreados")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.IngresarAsync(cama.Id, ClienteApi.NuevoDni())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync("/api/v1/pacientes")).StatusCode);
    }

    [Fact]
    public async Task LaEnfermeraRecibe403AlRegistrarDispositivos()
    {
        var enfermera = await ClienteEnfermeraAsync();

        var respuesta = await enfermera.PostAsJsonAsync("/api/v1/dispositivos", new { codigo = "ESP32-NO-PERMITIDO" });

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task ElMedicoRecibe403AlIngresarPacientes()
    {
        var cama = await (await ClienteAdministradorAsync()).CrearCamaAsync();
        var medico = await ClienteMedicoAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await medico.IngresarAsync(cama.Id, ClienteApi.NuevoDni())).StatusCode);
    }

    [Fact]
    public async Task ElAdministradorVeLaOcupacionDeLaCamaPeroNoQuienLaOcupa()
    {
        var administrador = await ClienteAdministradorAsync();
        var cama = await administrador.CrearCamaAsync();
        var enfermera = await ClienteEnfermeraAsync();
        var ficha = await enfermera.IngresarPacienteAsync(cama.Id);

        var cuerpo = await administrador.GetStringAsync("/api/v1/camas");

        Assert.Contains($"\"codigo\":\"{cama.Codigo}\"", cuerpo);
        Assert.Contains("\"ocupada\":true", cuerpo);
        Assert.DoesNotContain(ficha.NombreCompleto, cuerpo);
        Assert.DoesNotContain(ficha.Id.ToString(), cuerpo);
    }

    [Fact]
    public async Task ElPersonalClinicoLeeCamasYDispositivos()
    {
        var medico = await ClienteMedicoAsync();

        Assert.Equal(HttpStatusCode.OK, (await medico.GetAsync("/api/v1/camas?soloDisponibles=true")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await medico.GetAsync("/api/v1/dispositivos?estado=Disponible")).StatusCode);
    }
}
