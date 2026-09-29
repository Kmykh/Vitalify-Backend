using System.Diagnostics;
using Vitalify.Api.Configuracion;
using Vitalify.Infrastructure;

// Carga el .env de la raíz del repositorio como variables de entorno (ConnectionStrings__X => ConnectionStrings:X).
// En Docker no hay .env dentro de la imagen: las variables llegan por env_file.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.UseUtcTimestamp = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddControllers();
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    ctx.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier));
builder.Services.AddExceptionHandler<ManejadorGlobalDeExcepciones>();
builder.Services.AddSwaggerVitalify();
builder.Services.AddCorsVitalify(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "Vitalify API");
}

app.UseCors(CorsVitalify.Politica);
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health", RespuestaHealth.Opciones);

app.Run();

/// <summary>Punto de entrada. Parcial y público para poder usarlo con WebApplicationFactory en pruebas.</summary>
public partial class Program;
