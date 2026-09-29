using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Vitalify.Api.Configuracion;

/// <summary>
/// Convierte toda excepción no controlada en un ProblemDetails 500.
/// El mensaje y el stack trace solo se incluyen en Development.
/// </summary>
internal sealed class ManejadorGlobalDeExcepciones(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<ManejadorGlobalDeExcepciones> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Excepción no controlada en {Metodo} {Ruta}", httpContext.Request.Method, httpContext.Request.Path);

        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Ocurrió un error inesperado.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Instance = httpContext.Request.Path,
        };

        if (environment.IsDevelopment())
        {
            problema.Detail = exception.Message;
            problema.Extensions["excepcion"] = exception.ToString();
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problema,
            Exception = exception,
        });
    }
}
