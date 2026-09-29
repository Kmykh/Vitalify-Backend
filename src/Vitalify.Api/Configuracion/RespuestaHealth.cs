using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Vitalify.Api.Configuracion;

/// <summary>
/// Respuesta JSON de <c>/health</c> con el estado global y el de cada check (<c>transaccional</c>, <c>historial</c>).
/// </summary>
internal static class RespuestaHealth
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static HealthCheckOptions Opciones { get; } = new() { ResponseWriter = EscribirAsync };

    private static Task EscribirAsync(HttpContext context, HealthReport reporte)
    {
        var mostrarErrores = context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();

        var cuerpo = new
        {
            estado = reporte.Status.ToString(),
            duracionMs = Math.Round(reporte.TotalDuration.TotalMilliseconds),
            checks = reporte.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    estado = e.Value.Status.ToString(),
                    duracionMs = Math.Round(e.Value.Duration.TotalMilliseconds),
                    error = mostrarErrores ? e.Value.Exception?.Message : null,
                }),
        };

        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(cuerpo, Json));
    }
}
