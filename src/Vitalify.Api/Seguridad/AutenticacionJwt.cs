using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Vitalify.Api.Configuracion;
using Vitalify.Application.Puertos;
using Vitalify.Infrastructure.Seguridad;

namespace Vitalify.Api.Seguridad;

internal static class AutenticacionJwt
{
    /// <summary>
    /// Valida emisor, audiencia, firma (solo HS256) y expiración con 30 s de tolerancia. Además rechaza los
    /// tokens cuyo <c>jti</c> se revocó en un logout. Los 401 salen como ProblemDetails en español.
    /// </summary>
    public static IServiceCollection AddAutenticacionVitalify(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<OpcionesJwt>((o, jwt) =>
        {
            // Se conservan los nombres de claim del token (sub, role, name...) sin mapearlos a los de .NET.
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Emisor,
                ValidateAudience = true,
                ValidAudience = jwt.Audiencia,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = jwt.ClaveDeFirma(),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                RequireSignedTokens = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = ClaimsVitalify.Nombre,
                RoleClaimType = ClaimsVitalify.Rol,
            };
            o.Events = new JwtBearerEvents
            {
                OnMessageReceived = TokenDelHubDesdeElQuery,
                OnTokenValidated = RechazarSiFueRevocadoAsync,
                OnChallenge = ResponderNoAutenticadoAsync,
            };
        });

        return services;
    }

    /// <summary>
    /// Los WebSockets del navegador no pueden mandar el encabezado Authorization: solo para el hub de monitoreo se acepta
    /// el token en <c>?access_token=</c>. En el resto de la API, el query string nunca autentica.
    /// </summary>
    private static Task TokenDelHubDesdeElQuery(MessageReceivedContext context)
    {
        if (context.HttpContext.Request.Path.StartsWithSegments(TiempoReal.HubMonitoreo.Ruta)
            && context.Request.Query.TryGetValue("access_token", out var token) && !string.IsNullOrEmpty(token))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    }

    private static async Task RechazarSiFueRevocadoAsync(TokenValidatedContext context)
    {
        var jti = context.Principal?.Jti();
        var revocados = context.HttpContext.RequestServices.GetRequiredService<IRepositorioTokensRevocados>();

        if (string.IsNullOrEmpty(jti) || await revocados.EstaRevocadoAsync(jti, context.HttpContext.RequestAborted))
        {
            context.Fail(new TokenRevocadoException());
        }
    }

    private static async Task ResponderNoAutenticadoAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        context.Response.Headers.WWWAuthenticate = "Bearer";

        var (tipo, detalle) = context.AuthenticateFailure switch
        {
            null => ("no-autenticado", "Se requiere un token de acceso."),
            TokenRevocadoException => ("sesion-cerrada", "La sesión de este token fue cerrada. Inicia sesión de nuevo."),
            SecurityTokenExpiredException => ("token-expirado", "El token de acceso expiró. Renuévalo con /api/v1/auth/refresh."),
            _ => ("token-invalido", "El token de acceso no es válido."),
        };

        await RespuestasProblema.EscribirAsync(context.HttpContext, StatusCodes.Status401Unauthorized, tipo, "No autenticado", detalle);
    }

    private sealed class TokenRevocadoException() : Exception("El token fue revocado.");
}
