using System.Net;
using System.Net.Http.Json;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Sistema;

namespace Vitalify.Api.IntegrationTests;

/// <summary>Fase 7, parte B: lo que la API necesita para producción (almacenamiento, proxy, CORS y SignalR).</summary>
public class Fase7ProduccionTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task ElAdministradorVeElTamanoDeCadaTablaParaVigilarLos500Mb()
    {
        var administrador = await ClienteAdministradorAsync();

        var almacenamiento = (await administrador.GetFromJsonAsync<AlmacenamientoDto>("/api/v1/sistema/almacenamiento"))!;

        Assert.Equal(500L * 1024 * 1024, almacenamiento.LimiteBytes);
        Assert.True(almacenamiento.BytesBase > 0);
        Assert.True(almacenamiento.BytesVitalify <= almacenamiento.BytesBase);
        Assert.Contains(almacenamiento.Tablas, t => (t.Contexto, t.Esquema, t.Tabla) == ("transaccional", "vitalify", "usuario"));
        Assert.Contains(almacenamiento.Tablas, t => (t.Contexto, t.Esquema, t.Tabla) == ("historial", "vitalify_historial", "lectura_signos"));
        Assert.Contains(almacenamiento.Tablas, t => t.Tabla == "lectura_signos_resumen");
        Assert.All(almacenamiento.Tablas, t => Assert.True(t.Bytes > 0));
        Assert.Equal(almacenamiento.Tablas.OrderByDescending(t => t.Bytes).Select(t => t.Bytes), almacenamiento.Tablas.Select(t => t.Bytes));
    }

    [Fact]
    public async Task ElPersonalClinicoNoVeElAlmacenamiento()
    {
        var enfermera = await ClienteEnfermeraAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await enfermera.GetAsync("/api/v1/sistema/almacenamiento")).StatusCode);
    }
}
