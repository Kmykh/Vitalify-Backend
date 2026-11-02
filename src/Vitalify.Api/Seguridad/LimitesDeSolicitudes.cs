using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Vitalify.Api.Configuracion;

namespace Vitalify.Api.Seguridad;

/// <summary>Límites de frecuencia. Al superarlos, la API responde 429 con <c>Retry-After</c>.</summary>
internal static class LimitesDeSolicitudes
{
    /// <summary><c>POST /auth/login</c>: por IP (<c>RateLimit:LoginIntentosPorMinuto</c>, 5 por defecto).</summary>
    public const string Login = "login";

    /// <summary><c>POST /vinculaciones/escaneo</c>: por usuario (<c>RateLimit:EscaneoQrPorMinuto</c>, 10 por defecto).</summary>
    public const string EscaneoQr = "escaneo-qr";

    public static IServiceCollection AddLimitesDeSolicitudes(this IServiceCollection services, IConfiguration configuration)
    {
        var intentosLogin = configuration.GetValue("RateLimit:LoginIntentosPorMinuto", 5);
        var escaneos = configuration.GetValue("RateLimit:EscaneoQrPorMinuto", 10);

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Login, context => VentanaDeUnMinuto(context.Ip() ?? "desconocida", intentosLogin));

            // Después de la autenticación: la partición es el usuario (claim sub), no la IP de la tablet.
            o.AddPolicy(EscaneoQr, context => VentanaDeUnMinuto(context.User.IdUsuario()?.ToString() ?? context.Ip() ?? "desconocido", escaneos));

            o.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(espera.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var politica = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                var detalle = politica == EscaneoQr
                    ? "Superaste el límite de escaneos de QR por minuto. Espera un momento."
                    : "Superaste el límite de intentos de inicio de sesión. Espera un minuto.";
                await RespuestasProblema.EscribirAsync(
                    context.HttpContext, StatusCodes.Status429TooManyRequests, "demasiados-intentos", "Demasiados intentos", detalle);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> VentanaDeUnMinuto(string clave, int permitidos) =>
        RateLimitPartition.GetFixedWindowLimiter(clave, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitidos,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
}
