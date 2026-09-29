using Microsoft.AspNetCore.Mvc;

namespace Vitalify.Api.Configuracion;

/// <summary>Escribe ProblemDetails en español fuera de MVC (401, 403, 429).</summary>
internal static class RespuestasProblema
{
    public static async Task EscribirAsync(HttpContext context, int status, string tipo, string titulo, string detalle)
    {
        context.Response.StatusCode = status;
        var servicio = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await servicio.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Type = tipo,
                Title = titulo,
                Detail = detalle,
                Instance = context.Request.Path,
            },
        });
    }
}
