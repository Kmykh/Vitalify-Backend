namespace Vitalify.Api.Configuracion;

internal static class CorsVitalify
{
    /// <summary>La API REST: sin credenciales (el JWT va en el encabezado Authorization, no en cookies).</summary>
    public const string Politica = "Vitalify";

    /// <summary>
    /// Solo el hub de SignalR: el cliente JavaScript negocia con <c>withCredentials: true</c> por defecto, y el navegador
    /// exige <c>Access-Control-Allow-Credentials</c>. Por eso lleva AllowCredentials, y solo con orígenes explícitos.
    /// </summary>
    public const string PoliticaSignalR = "Vitalify-SignalR";

    /// <summary>
    /// Orígenes del dashboard desde <c>Cors:Origenes</c>: una lista en appsettings o <c>Cors__Origenes</c> en el .env con
    /// los orígenes separados por coma (por ejemplo <c>https://dashboard.example.com</c>). Nunca <c>*</c>: la API no
    /// arranca si aparece.
    /// </summary>
    public static IServiceCollection AddCorsVitalify(this IServiceCollection services, IConfiguration configuration)
    {
        var origenes = LeerOrigenes(configuration);

        services.AddCors(o =>
        {
            o.AddPolicy(Politica, p => p.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod());
            o.AddPolicy(PoliticaSignalR, p => p.WithOrigins(origenes).AllowAnyHeader().WithMethods("GET", "POST").AllowCredentials());
        });

        return services;
    }

    internal static string[] LeerOrigenes(IConfiguration configuration)
    {
        var seccion = configuration.GetSection("Cors:Origenes");
        var origenes = seccion.GetChildren().Select(h => h.Value).Append(seccion.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .SelectMany(v => v!.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(o => o.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (origenes.Any(o => o.Contains('*', StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Cors__Origenes no admite '*': lista los orígenes del dashboard uno por uno.");
        }

        return origenes;
    }
}
