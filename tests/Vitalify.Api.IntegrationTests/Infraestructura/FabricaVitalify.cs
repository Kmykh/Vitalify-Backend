using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Vitalify.Application.Puertos;
using Vitalify.Application.Vinculacion;
using Vitalify.Infrastructure.Persistence;
using Vitalify.Infrastructure.Persistence.Historial;
using Vitalify.Infrastructure.Persistence.Transaccional;

namespace Vitalify.Api.IntegrationTests.Infraestructura;

/// <summary>
/// Levanta la API contra un PostgreSQL en contenedor (nunca contra Supabase). La configuración se pasa como
/// variables de entorno, que tienen prioridad sobre el .env del desarrollador porque Program.cs lo carga con
/// NoClobber. Las migraciones se aplican antes de arrancar la API para que la semilla encuentre las tablas.
/// </summary>
public class FabricaVitalify : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminCorreo = "admin@vitalify.test";
    public const string AdminContrasena = "AdminPrueba123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private int _ultimaIp;

    public RelojAjustable Reloj { get; } = new();

    /// <summary>
    /// Comandos que el backend publicó al wearable (tópico, JSON y si va retenido). Envuelve al adaptador real: en las
    /// fábricas sin MQTT solo los registra; en <see cref="FabricaVitalifyMqtt"/>, además los publica en el broker.
    /// </summary>
    public ComandosRegistrados Comandos { get; } = new();

    /// <summary>Push enviados (envuelve al adaptador real, que solo escribe en el log).</summary>
    public PushRegistrado Push { get; } = new();

    /// <summary>Entorno de ASP.NET Core con el que arranca la API.</summary>
    protected virtual string Entorno => "Development";

    /// <summary>Red de los proxies conocidos en las pruebas (<c>Proxy__RedesConocidas</c>).</summary>
    public const string RedProxy = "10.99.0.0/16";

    /// <summary>La IP con la que llega una solicitud que pasó por el proxy conocido.</summary>
    public const string IpProxy = "10.99.0.2";

    /// <summary>
    /// Si los clientes entran como en el servidor: por el proxy conocido, con <c>X-Forwarded-For</c> (su IP) y
    /// <c>X-Forwarded-Proto: https</c>. Fuera de Development la API redirige a HTTPS lo que no llega así.
    /// </summary>
    protected virtual bool ClientesPorElProxy => false;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var cadena = _postgres.GetConnectionString();

        var configuracion = new Dictionary<string, string>
        {
            ["ConnectionStrings__Transaccional"] = cadena,
            ["ConnectionStrings__Historial"] = cadena,
            ["Jwt__Emisor"] = "vitalify-api",
            ["Jwt__Audiencia"] = "vitalify-clientes",
            ["Jwt__Clave"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Jwt__MinutosAccessToken"] = "15",
            ["Jwt__MinutosInactividad"] = "30",
            ["Jwt__HorasMaximasSesion"] = "12",
            ["Seed__AdminCorreo"] = AdminCorreo,
            ["Seed__AdminNombre"] = "Administrador de pruebas",
            ["Seed__AdminContrasena"] = AdminContrasena,
            ["RateLimit__LoginIntentosPorMinuto"] = "5",
            ["Simulador__Habilitado"] = "false",
            ["Seed__DatosDemo"] = "false",
            ["Mqtt__Habilitado"] = "false",
            // La configuración va por variables del proceso: se restablece lo que otra fábrica pudo dejar (TLS sin validar).
            ["Mqtt__UsarTls"] = "false",
            ["Mqtt__ValidarCertificado"] = "true",
            ["Clinico__AjusteTemperaturaSensor"] = "0",
            ["Vinculacion__SegundosRenovacion"] = "0", // el worker de renovación se prueba llamando al caso de uso
            ["Alertas__SegundosVigilancia"] = "0", // el vigilante también: Fabrica.VigilarAlertasAsync()
            ["Push__Habilitado"] = "false",
            ["Historial__RetencionHabilitada"] = "false", // la retención también: Fabrica.ResumirHistorialAsync()
            ["Proxy__RedesConocidas"] = RedProxy, // solo los "proxies" de esta red pueden indicar X-Forwarded-For
            ["Cors__Origenes"] = "http://localhost:5173",
        };
        foreach (var (clave, valor) in await ConfiguracionAdicionalAsync())
        {
            configuracion[clave] = valor;
        }

        foreach (var (clave, valor) in configuracion)
        {
            Environment.SetEnvironmentVariable(clave, valor);
        }

        await MigrarAsync<TransaccionalDbContext>(cadena, TransaccionalDbContext.Esquema, o => new TransaccionalDbContext(o));
        await MigrarAsync<HistorialDbContext>(cadena, HistorialDbContext.Esquema, o => new HistorialDbContext(o));
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await LiberarAdicionalAsync();
    }

    /// <summary>Para las fábricas derivadas: levantar otros contenedores y agregar variables de entorno.</summary>
    protected virtual Task<IReadOnlyDictionary<string, string>> ConfiguracionAdicionalAsync() =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

    protected virtual Task LiberarAdicionalAsync() => Task.CompletedTask;

    /// <summary>Cliente HTTP con una IP propia, para que el límite de intentos de login no se comparta entre pruebas.</summary>
    public HttpClient CrearCliente(string? ip = null)
    {
        var cliente = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var n = Interlocked.Increment(ref _ultimaIp);
        var ipCliente = ip ?? $"10.1.{n / 250}.{(n % 250) + 1}";
        if (ClientesPorElProxy)
        {
            cliente.DefaultRequestHeaders.Add(FiltroIpDePrueba.Encabezado, IpProxy);
            cliente.DefaultRequestHeaders.Add("X-Forwarded-For", ipCliente);
            cliente.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        }
        else
        {
            cliente.DefaultRequestHeaders.Add(FiltroIpDePrueba.Encabezado, ipCliente);
        }

        return cliente;
    }

    public async Task<T> ConsultarAsync<T>(Func<TransaccionalDbContext, Task<T>> consulta)
    {
        await using var scope = Services.CreateAsyncScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<TransaccionalDbContext>());
    }

    public async Task<T> ConsultarHistorialAsync<T>(Func<HistorialDbContext, Task<T>> consulta)
    {
        await using var scope = Services.CreateAsyncScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<HistorialDbContext>());
    }

    /// <summary>Lo que hace el receptor MQTT cuando llega <c>online</c>/<c>offline</c> en <c>device/{codigo}/estado</c>.</summary>
    public async Task SimularEstadoAsync(string codigoDispositivo, string estado = "online")
    {
        await using var scope = Services.CreateAsyncScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<RegistrarPresenciaDispositivo>().EjecutarAsync(codigoDispositivo, estado));
    }

    /// <summary>Corre una vez lo que hace el vigilante de alertas.</summary>
    public async Task<Application.Alertas.VigilarAlertas.Resumen> VigilarAlertasAsync(DateTime? ultimaRevision = null)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Application.Alertas.VigilarAlertas>().EjecutarAsync(ultimaRevision);
    }

    /// <summary>Corre una vez lo que hace el worker de retención del historial (a las 03:00 de Lima).</summary>
    public async Task<Application.Historial.ResultadoRetencion> ResumirHistorialAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<Application.Historial.ResumirLecturasAntiguas>().EjecutarAsync();
    }

    /// <summary>Corre una vez lo que hace el worker de renovación de códigos.</summary>
    public async Task<int> RenovarCodigosAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RenovarCodigosVinculacion>().EjecutarAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Entorno);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReloj>();
            services.AddSingleton<IReloj>(Reloj);

            var publicador = services.Last(d => d.ServiceType == typeof(IPublicadorComandosDispositivo));
            services.RemoveAll<IPublicadorComandosDispositivo>();
            services.AddSingleton<IPublicadorComandosDispositivo>(sp =>
            {
                Comandos.Interno = (IPublicadorComandosDispositivo)ActivatorUtilities.CreateInstance(sp, publicador.ImplementationType!);
                return Comandos;
            });

            var push = services.Last(d => d.ServiceType == typeof(INotificadorPush));
            services.RemoveAll<INotificadorPush>();
            services.AddSingleton<INotificadorPush>(sp =>
            {
                Push.Interno = (INotificadorPush)ActivatorUtilities.CreateInstance(sp, push.ImplementationType!);
                return Push;
            });

            services.AddSingleton<IStartupFilter, FiltroIpDePrueba>();
        });
    }

    private static async Task MigrarAsync<TContexto>(string cadena, string esquema, Func<DbContextOptions<TContexto>, TContexto> crear)
        where TContexto : DbContext
    {
        var opciones = new DbContextOptionsBuilder<TContexto>();
        OpcionesNpgsql.Configurar(opciones, cadena, esquema);
        await using var contexto = crear(opciones.Options);
        await contexto.Database.MigrateAsync();
    }
}

/// <summary>Toma la IP del cliente del encabezado <see cref="Encabezado"/> (solo en pruebas).</summary>
internal sealed class FiltroIpDePrueba : IStartupFilter
{
    public const string Encabezado = "X-Ip-Prueba";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, siguiente) =>
        {
            if (context.Request.Headers.TryGetValue(Encabezado, out var valor) && IPAddress.TryParse(valor, out var ip))
            {
                context.Connection.RemoteIpAddress = ip;
            }

            await siguiente();
        });
        next(app);
    };
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionApi : ICollectionFixture<FabricaVitalify>
{
    public const string Nombre = "api";
}

/// <summary>Base de datos propia (otro contenedor) para las pruebas que necesitan partir sin datos.</summary>
public sealed class FabricaVitalifyAislada : FabricaVitalify;

[CollectionDefinition(Nombre)]
public sealed class ColeccionApiAislada : ICollectionFixture<FabricaVitalifyAislada>
{
    public const string Nombre = "api-aislada";
}

/// <summary>Base propia para la retención: resume (y borra las crudas de) todo episodio egresado hace más de 30 días.</summary>
public sealed class FabricaVitalifyRetencion : FabricaVitalify;

[CollectionDefinition(Nombre)]
public sealed class ColeccionApiRetencion : ICollectionFixture<FabricaVitalifyRetencion>
{
    public const string Nombre = "api-retencion";
}

/// <summary>
/// La API en entorno Production (su propio contenedor), para lo que solo existe en Development. Los clientes entran por
/// el proxy con HTTPS, como en el servidor. Arranca con <c>Simulador__Habilitado=true</c> para comprobar que se ignora.
/// </summary>
public sealed class FabricaVitalifyProduccion : FabricaVitalify
{
    protected override string Entorno => "Production";

    protected override bool ClientesPorElProxy => true;

    protected override Task<IReadOnlyDictionary<string, string>> ConfiguracionAdicionalAsync() =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>
        {
            ["Simulador__Habilitado"] = "true",
            ["Cors__Origenes"] = "https://dashboard.vitalify.test",
        });
}

[CollectionDefinition(Nombre)]
public sealed class ColeccionApiProduccion : ICollectionFixture<FabricaVitalifyProduccion>
{
    public const string Nombre = "api-produccion";
}

public sealed record ComandoRegistrado(string Dispositivo, string Topico, ComandoDispositivo Comando, string Json, bool Retenido);

/// <summary>Registra cada comando al wearable (con el tópico y el JSON del contrato) y lo pasa al adaptador real.</summary>
public sealed class ComandosRegistrados : IPublicadorComandosDispositivo
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<ComandoRegistrado> _comandos = new();

    internal IPublicadorComandosDispositivo? Interno { get; set; }

    /// <summary>Comandos al dispositivo, en el orden en que se publicaron.</summary>
    public IReadOnlyList<ComandoRegistrado> De(string dispositivo) => _comandos.Where(c => c.Dispositivo == dispositivo).ToList();

    public IReadOnlyList<string> AccionesDe(string dispositivo) => De(dispositivo).Select(c => c.Comando.Accion).ToList();

    /// <summary>Lo que dibujaría la OLED: el contenido del último <c>mostrar-qr</c>.</summary>
    public string UltimoQr(string dispositivo) => De(dispositivo).Select(c => c.Comando).OfType<MostrarQr>().Last().Qr;

    public async Task PublicarAsync(string codigoDispositivo, ComandoDispositivo comando, bool retenido, CancellationToken ct = default)
    {
        _comandos.Enqueue(new ComandoRegistrado(
            codigoDispositivo, ContratoComandos.Topico(codigoDispositivo), comando, ContratoComandos.SerializarTexto(comando), retenido));
        if (Interno is not null)
        {
            await Interno.PublicarAsync(codigoDispositivo, comando, retenido, ct);
        }
    }
}

/// <summary>
/// Registra cada push (tokens y mensaje) y lo pasa al adaptador real. Los tokens de <see cref="NoRegistrados"/> responden
/// "no registrado", como FCM con una app desinstalada.
/// </summary>
public sealed class PushRegistrado : INotificadorPush
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(IReadOnlyList<string> Tokens, MensajePush Mensaje)> _enviados = new();

    public System.Collections.Concurrent.ConcurrentDictionary<string, bool> NoRegistrados { get; } = new();

    internal INotificadorPush? Interno { get; set; }

    public IReadOnlyList<(IReadOnlyList<string> Tokens, MensajePush Mensaje)> Enviados => _enviados.ToList();

    /// <summary>Los push de una alerta, en orden.</summary>
    public IReadOnlyList<(IReadOnlyList<string> Tokens, MensajePush Mensaje)> DeAlerta(Guid alertaId) =>
        _enviados.Where(e => e.Mensaje.Datos.GetValueOrDefault("alertaId") == alertaId.ToString()).ToList();

    public async Task<IReadOnlyList<ResultadoEnvioPush>> EnviarAsync(IReadOnlyList<string> tokens, MensajePush mensaje, CancellationToken ct = default)
    {
        _enviados.Enqueue((tokens, mensaje));
        var resultados = Interno is null ? tokens.Select(_ => ResultadoEnvioPush.Enviado).ToList() : await Interno.EnviarAsync(tokens, mensaje, ct);
        return tokens.Select((token, i) => NoRegistrados.ContainsKey(token) ? ResultadoEnvioPush.NoRegistrado : resultados[i]).ToList();
    }
}
