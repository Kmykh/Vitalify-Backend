using System.Text.Json;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Comun;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Api.Entrada.Desarrollo;

/// <summary>
/// Adaptador de entrada temporal: recibe el JSON del contrato de telemetría por HTTP y lo pasa al mismo puerto
/// que usarán el simulador y, en la fase 7, MQTT. Solo existe en Development.
/// </summary>
internal static class EndpointTelemetriaDesarrollo
{
    public const string Ruta = "/api/v1/dev/telemetria";

    public static IEndpointRouteBuilder MapTelemetriaDesarrollo(this IEndpointRouteBuilder app)
    {
        app.MapPost(Ruta, RecibirAsync)
            .RequireAuthorization(Politicas.SoloAdministrador)
            .WithTags("Desarrollo")
            .WithSummary("Envía una lectura de telemetría como lo haría el ESP32 (solo Development).")
            .WithDescription("Recibe el JSON de docs/contrato-telemetria.md y responde el resultado de la ingesta: Aceptada, AceptadaParcial, Duplicada o Rechazada, con sus incidencias.")
            .Produces<ResultadoIngesta>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> RecibirAsync(JsonElement mensaje, IRegistrarLectura registrarLectura, CancellationToken ct)
    {
        var lectura = MensajeTelemetria.Interpretar(mensaje, OrigenLectura.ApiDesarrollo);
        if (!lectura.EsExito)
        {
            return AProblema(lectura.Error);
        }

        var resultado = await registrarLectura.EjecutarAsync(lectura.Valor, ct);
        return resultado.EsExito ? TypedResults.Ok(resultado.Valor) : AProblema(resultado.Error);
    }

    private static IResult AProblema(Error error) =>
        error.Tipo == TipoError.Validacion
            ? TypedResults.ValidationProblem(error.Detalles.ToDictionary(), title: error.Mensaje, type: error.Codigo)
            : TypedResults.Problem(error.Mensaje, statusCode: StatusCodes.Status409Conflict, title: "Conflicto", type: error.Codigo);
}
