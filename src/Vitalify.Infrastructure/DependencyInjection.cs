using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vitalify.Infrastructure.Persistence;
using Vitalify.Infrastructure.Persistence.Historial;
using Vitalify.Infrastructure.Persistence.Transaccional;

namespace Vitalify.Infrastructure;

public static class DependencyInjection
{
    private static readonly TimeSpan TimeoutHealthCheck = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registra los adaptadores de salida: los dos DbContext y sus health checks
    /// (<c>transaccional</c> e <c>historial</c>).
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

        return services;
    }
}
