using Microsoft.OpenApi.Models;

namespace Vitalify.Api.Configuracion;

internal static class SwaggerVitalify
{
    private const string EsquemaBearer = "Bearer";

    /// <summary>
    /// Swagger con comentarios XML y el esquema Bearer JWT (botón "Authorize"), que se usará desde la fase 1.
    /// </summary>
    public static IServiceCollection AddSwaggerVitalify(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Vitalify API",
                Version = "v1",
                Description = "Plataforma IoMT hospitalaria: monitoreo de signos vitales, NEWS2/MEWS y alertas.",
            });

            o.AddSecurityDefinition(EsquemaBearer, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Name = "Authorization",
                Description = "Token JWT emitido por /api/v1/auth (fase 1). Pega solo el token, sin el prefijo \"Bearer\".",
            });
            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = EsquemaBearer },
                }] = [],
            });

            var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
            o.IncludeXmlComments(xml);
        });

        return services;
    }
}
