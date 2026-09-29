namespace Vitalify.Api.Configuracion;

internal static class CorsVitalify
{
    public const string Politica = "Vitalify";

    /// <summary>
    /// Orígenes permitidos desde <c>Cors:OrigenesPermitidos</c> en appsettings
    /// (por defecto, el dashboard React en desarrollo: http://localhost:5173).
    /// </summary>
    public static IServiceCollection AddCorsVitalify(this IServiceCollection services, IConfiguration configuration)
    {
        var origenes = configuration.GetSection("Cors:OrigenesPermitidos").Get<string[]>() ?? [];

        services.AddCors(o => o.AddPolicy(Politica, p => p
            .WithOrigins(origenes)
            .AllowAnyHeader()
            .AllowAnyMethod()));

        return services;
    }
}
