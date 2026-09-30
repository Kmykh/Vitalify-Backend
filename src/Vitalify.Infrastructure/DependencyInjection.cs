using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vitalify.Application.Puertos;
using Vitalify.Application.Sesiones;
using Vitalify.Infrastructure.Persistence;
using Vitalify.Infrastructure.Persistence.Historial;
using Vitalify.Infrastructure.Persistence.Transaccional;
using Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;
using Vitalify.Infrastructure.Seguridad;
using Vitalify.Infrastructure.Semillas;

namespace Vitalify.Infrastructure;

public static class DependencyInjection
{
    private static readonly TimeSpan TimeoutHealthCheck = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registra los adaptadores de salida: los dos DbContext y sus health checks (<c>transaccional</c> e
    /// <c>historial</c>), los repositorios, la seguridad (hasher, tokens, reloj) y las semillas (administrador y
    /// datos de demostración).
    /// Falla al arrancar si falta una cadena de conexión o la configuración JWT es inválida.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var transaccional = CadenaDeConexion.Obtener(configuration, CadenaDeConexion.Transaccional);
        var historial = CadenaDeConexion.Obtener(configuration, CadenaDeConexion.Historial);

        services.AddDbContext<TransaccionalDbContext>(o =>
            OpcionesNpgsql.Configurar(o, transaccional, TransaccionalDbContext.Esquema));
        services.AddDbContext<HistorialDbContext>(o =>
            OpcionesNpgsql.Configurar(o, historial, HistorialDbContext.Esquema));

        services.AddHealthChecks()
            .AddNpgSql(transaccional, name: "transaccional", tags: ["db"], timeout: TimeoutHealthCheck)
            .AddNpgSql(historial, name: "historial", tags: ["db"], timeout: TimeoutHealthCheck);

        var jwt = OpcionesJwt.Cargar(configuration);
        services.AddSingleton(jwt);
        services.AddSingleton(new OpcionesSesion(
            TimeSpan.FromMinutes(jwt.MinutosInactividad), TimeSpan.FromHours(jwt.HorasMaximasSesion)));

        services.AddMemoryCache();
        services.AddSingleton<IReloj, RelojSistema>();
        services.AddSingleton<IHasherContrasenas, HasherContrasenas>();
        services.AddSingleton<IGeneradorTokens, GeneradorTokens>();

        services.AddScoped<IRepositorioUsuarios, RepositorioUsuarios>();
        services.AddScoped<IRepositorioSesiones, RepositorioSesiones>();
        services.AddScoped<IRepositorioTokensRevocados, RepositorioTokensRevocados>();
        services.AddScoped<IRepositorioAuditoria, RepositorioAuditoria>();
        services.AddScoped<IRepositorioCamas, RepositorioCamas>();
        services.AddScoped<IRepositorioDispositivos, RepositorioDispositivos>();
        services.AddScoped<IRepositorioPacientes, RepositorioPacientes>();
        services.AddScoped<IRepositorioHospitalizaciones, RepositorioHospitalizaciones>();
        services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();

        services.AddHostedService<SemillaAdministrador>();
        services.AddHostedService<SemillaDatosDemo>();

        return services;
    }
}
