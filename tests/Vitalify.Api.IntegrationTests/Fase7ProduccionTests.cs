using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Vitalify.Api.Entrada.Simulador;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Sistema;
using Vitalify.Domain.Auditoria;

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

    /// <summary>Cinco logins fallidos que llegan por <paramref name="proxy"/> diciendo venir de <paramref name="cliente"/>.</summary>
    private async Task<HttpClient> AgotarLoginsAsync(string proxy, string cliente)
    {
        var http = Fabrica.CrearCliente(proxy);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginDesdeAsync(http, cliente)).StatusCode);
        }

        return http;
    }

    private static Task<HttpResponseMessage> LoginDesdeAsync(HttpClient http, string ipCliente)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { correo = $"nadie.{Guid.NewGuid():N}@vitalify.test", contrasena = "Incorrecta99" }),
        };
        solicitud.Headers.Add("X-Forwarded-For", ipCliente);
        return http.SendAsync(solicitud);
    }

    [Fact]
    public async Task DesdeUnProxyConocido_ElLimiteDeLoginsSeCuentaPorLaIpDeXForwardedFor_YAsiSeAudita()
    {
        var http = await AgotarLoginsAsync(FabricaVitalify.IpProxy, "198.51.100.10");

        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginDesdeAsync(http, "198.51.100.10")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginDesdeAsync(http, "198.51.100.11")).StatusCode); // otro cliente, mismo proxy
        Assert.Equal(5, await Fabrica.ConsultarAsync(db => db.Auditoria.CountAsync(a => a.Accion == AccionAuditoria.LoginFallido && a.Ip == "198.51.100.10")));
    }

    [Fact]
    public async Task DesdeUnProxyDesconocido_XForwardedForSeIgnora_YElLimiteSeCuentaPorSuIp()
    {
        var http = await AgotarLoginsAsync("203.0.113.9", "198.51.100.20");

        // Cambiar el encabezado no sirve para saltarse el límite: la IP es la de la conexión.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginDesdeAsync(http, "198.51.100.21")).StatusCode);
        Assert.Equal(0, await Fabrica.ConsultarAsync(db => db.Auditoria.CountAsync(a => a.Ip == "198.51.100.20")));
    }

    [Fact]
    public async Task Cors_LaApiNoAdmiteCredenciales_ElHubSi_YSoloParaLosOrigenesConfigurados()
    {
        var http = Fabrica.CrearCliente();

        async Task<HttpResponseMessage> PreflightAsync(string ruta, string origen)
        {
            var solicitud = new HttpRequestMessage(HttpMethod.Options, ruta);
            solicitud.Headers.Add("Origin", origen);
            solicitud.Headers.Add("Access-Control-Request-Method", "POST");
            solicitud.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
            return await http.SendAsync(solicitud);
        }

        var api = await PreflightAsync("/api/v1/auth/login", "http://localhost:5173");
        var hub = await PreflightAsync("/hubs/monitoreo/negotiate", "http://localhost:5173");
        var otro = await PreflightAsync("/api/v1/auth/login", "https://sitio-ajeno.example");

        Assert.Equal("http://localhost:5173", Assert.Single(api.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(api.Headers.Contains("Access-Control-Allow-Credentials"));
        Assert.Equal("http://localhost:5173", Assert.Single(hub.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(hub.Headers.GetValues("Access-Control-Allow-Credentials")));
        Assert.False(otro.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ElHubOfreceWebSockets()
    {
        var enfermera = await ClienteEnfermeraAsync();

        var respuesta = await enfermera.PostAsync("/hubs/monitoreo/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var negociacion = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var transportes = negociacion.RootElement.GetProperty("availableTransports").EnumerateArray()
            .Select(t => t.GetProperty("transport").GetString()).ToList();
        Assert.Equal(["WebSockets", "ServerSentEvents", "LongPolling"], transportes);
    }
}

/// <summary>ASPNETCORE_ENVIRONMENT=Production: sin rutas de desarrollo, sin simulador, sin Swagger y solo HTTPS.</summary>
[Collection(ColeccionApiProduccion.Nombre)]
public class ProduccionTests(FabricaVitalifyProduccion fabrica) : PruebaApi(fabrica)
{
    [Fact]
    public async Task EnProduccionNoExistenLasRutasDeDesarrolloNiElSimuladorNiSwagger()
    {
        var administrador = await ClienteAdministradorAsync();

        var telemetria = await administrador.PostAsJsonAsync("/api/v1/dev/telemetria", new { dispositivo = "ESP32-001", seq = 1, fc = 80 });
        var swagger = await administrador.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.NotFound, telemetria.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, swagger.StatusCode);
        // La fábrica arranca con Simulador__Habilitado=true: aun así, el simulador no se registra.
        Assert.DoesNotContain(Fabrica.Services.GetServices<IHostedService>(), s => s is SimuladorTelemetriaWorker);
        Assert.Null(Fabrica.Services.GetService<GeneradorLecturasSimuladas>());
    }

    [Fact]
    public async Task EnProduccionLoQueNoLlegaPorHttpsSeRedirige_YPorHttpsLlevaHsts()
    {
        // Directo, sin pasar por el proxy: HTTP plano.
        var directo = Fabrica.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var redirigida = await directo.GetAsync("/api/v1/ping");

        // Por el proxy, que ya terminó el HTTPS (X-Forwarded-Proto: https).
        var porProxy = Fabrica.CrearCliente();
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ping");
        solicitud.Headers.Host = "vitalify.example.com";
        var segura = await porProxy.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirigida.StatusCode);
        Assert.Equal("https", redirigida.Headers.Location!.Scheme);
        Assert.Equal(HttpStatusCode.OK, segura.StatusCode);
        Assert.Contains("max-age=31536000", Assert.Single(segura.Headers.GetValues("Strict-Transport-Security")), StringComparison.Ordinal);
    }
}

