using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Vitalify.Api.Configuracion;

internal static class SwaggerVitalify
{
    private const string EsquemaBearer = "Bearer";

    /// <summary>
    /// Swagger con comentarios XML y el esquema Bearer JWT (botón "Authorize"). El candado solo aparece en los
    /// endpoints que exigen autenticación.
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
                Description = "accessToken devuelto por POST /api/v1/auth/login. Pega solo el token, sin el prefijo \"Bearer\".",
            });
            o.OperationFilter<FiltroSeguridad>();

            var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
            o.IncludeXmlComments(xml);
        });

        return services;
    }

    private sealed class FiltroSeguridad : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            {
                return;
            }

            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = EsquemaBearer },
                    }] = [],
                },
            ];
        }
    }
}
