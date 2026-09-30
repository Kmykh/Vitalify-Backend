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
using Vitalify.Infrastructure.Persistence;
using Vitalify.Infrastructure.Persistence.Historial;
using Vitalify.Infrastructure.Persistence.Transaccional;

namespace Vitalify.Api.IntegrationTests.Infraestructura;

/// <summary>
/// Levanta la API contra un PostgreSQL en contenedor (nunca contra Supabase). La configuración se pasa como
/// variables de entorno, que tienen prioridad sobre el .env del desarrollador porque Program.cs lo carga con
/// NoClobber. Las migraciones se aplican antes de arrancar la API para que la semilla encuentre las tablas.
/// </summary>
public sealed class FabricaVitalify : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminCorreo = "admin@vitalify.test";
    public const string AdminContrasena = "AdminPrueba123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private int _ultimaIp;

    public RelojAjustable Reloj { get; } = new();

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
        };
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
    }

    /// <summary>Cliente HTTP con una IP propia, para que el límite de intentos de login no se comparta entre pruebas.</summary>
    public HttpClient CrearCliente(string? ip = null)
    {
        var cliente = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var n = Interlocked.Increment(ref _ultimaIp);
        cliente.DefaultRequestHeaders.Add(FiltroIpDePrueba.Encabezado, ip ?? $"10.1.{n / 250}.{(n % 250) + 1}");
        return cliente;
    }

    public async Task<T> ConsultarAsync<T>(Func<TransaccionalDbContext, Task<T>> consulta)
    {
        await using var scope = Services.CreateAsyncScope();
        return await consulta(scope.ServiceProvider.GetRequiredService<TransaccionalDbContext>());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReloj>();
            services.AddSingleton<IReloj>(Reloj);
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
