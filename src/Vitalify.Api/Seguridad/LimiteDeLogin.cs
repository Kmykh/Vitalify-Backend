using System.Globalization;
using System.Threading.RateLimiting;
using Vitalify.Api.Configuracion;

namespace Vitalify.Api.Seguridad;

internal static class LimiteDeLogin
{
    public const string Politica = "login";

    /// <summary>
    /// Ventana fija de un minuto por IP para <c>POST /auth/login</c>. El número de intentos se lee de
    /// <c>RateLimit:LoginIntentosPorMinuto</c> (5 por defecto). Al superarlo responde 429.
    /// </summary>
    public static IServiceCollection AddLimiteDeLogin(this IServiceCollection services, IConfiguration configuration)
    {
        var intentos = configuration.GetValue("RateLimit:LoginIntentosPorMinuto", 5);

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Politica, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Ip() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = intentos,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            o.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(espera.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                await RespuestasProblema.EscribirAsync(
                    context.HttpContext, StatusCodes.Status429TooManyRequests, "demasiados-intentos",
                    "Demasiados intentos", "Superaste el límite de intentos de inicio de sesión. Espera un minuto.");
            };
        });

        return services;
    }
}
