using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.Configuracion;
using Vitalify.Api.Seguridad;
using Vitalify.Application;
using Vitalify.Infrastructure;

// Carga el .env de la raíz del repositorio como variables de entorno (ConnectionStrings__X => ConnectionStrings:X).
// NoClobber: una variable de entorno ya definida (Docker, CI, pruebas) tiene prioridad sobre el .env.
// En Docker no hay .env dentro de la imagen: las variables llegan por env_file.
DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

// Los campos obligatorios los valida FluentValidation (mensajes en español), no el atributo implícito [Required].
builder.Services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.Configure<ApiBehaviorOptions>(o => o.InvalidModelStateResponseFactory = context =>
{
    var fabrica = context.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory>();
    var problema = fabrica.CreateValidationProblemDetails(
        context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest, "Los datos enviados no son válidos.", "validacion");
    return new BadRequestObjectResult(problema) { ContentTypes = { "application/problem+json" } };
});
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));
builder.Services.AddExceptionHandler<ManejadorGlobalDeExcepciones>();
builder.Services.AddSwaggerVitalify();
builder.Services.AddCorsVitalify(builder.Configuration);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAutenticacionVitalify();
builder.Services.AddAutorizacionVitalify();
builder.Services.AddLimiteDeLogin(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o =>
    {
        o.DocumentTitle = "Vitalify API";
        o.EnablePersistAuthorization();
    });
}

app.UseCors(CorsVitalify.Politica);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health", RespuestaHealth.Opciones).AllowAnonymous();

app.Run();

/// <summary>Punto de entrada. Parcial y público para poder usarlo con WebApplicationFactory en pruebas.</summary>
public partial class Program;
