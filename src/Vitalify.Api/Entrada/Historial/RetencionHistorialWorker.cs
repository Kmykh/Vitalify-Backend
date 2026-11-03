using Vitalify.Application.Historial;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Historial;

namespace Vitalify.Api.Entrada.Historial;

/// <summary>
/// Una vez al día, a las <see cref="OpcionesHistorial.HoraRetencion"/> (03:00) de la zona del hospital (America/Lima),
/// llama a <see cref="ResumirLecturasAntiguas"/>: las lecturas crudas de los episodios egresados hace más de 30 días pasan
/// a resúmenes de 5 min. Con <c>Historial__RetencionHabilitada=false</c> no hace nada. Si una vuelta falla, lo registra y
/// espera al día siguiente (la retención es idempotente).
/// </summary>
public sealed class RetencionHistorialWorker(
    OpcionesHistorial opciones, IReloj reloj, IServiceScopeFactory scopes, ILogger<RetencionHistorialWorker> logger) : BackgroundService
{
    /// <summary>Se recalcula la espera al menos cada hora, por si el reloj del servidor se ajusta.</summary>
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opciones.RetencionHabilitada)
        {
            logger.LogInformation("Retención del historial deshabilitada (Historial__RetencionHabilitada=false).");
            return;
        }

        var proxima = ProgramaDiario.Proxima(reloj.AhoraUtc, opciones.HoraRetencion, opciones.Zona);
        logger.LogInformation("Retención del historial: próxima vuelta {Proxima:O} (UTC).", proxima);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var falta = proxima - reloj.AhoraUtc;
                if (falta > TimeSpan.Zero)
                {
                    await Task.Delay(falta < EsperaMaxima ? falta : EsperaMaxima, stoppingToken);
                    continue;
                }

                await EjecutarAsync(stoppingToken);
                proxima = ProgramaDiario.Proxima(reloj.AhoraUtc, opciones.HoraRetencion, opciones.Zona);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La API se está deteniendo.
        }
    }

    private async Task EjecutarAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var resultado = await scope.ServiceProvider.GetRequiredService<ResumirLecturasAntiguas>().EjecutarAsync(ct);
            logger.LogInformation(
                "Retención del historial: {Episodios} episodios resumidos, {Lecturas} lecturas crudas pasaron a {Grupos} grupos de 5 min; {Fallidas} fallaron.",
                resultado.Hospitalizaciones, resultado.Lecturas, resultado.Grupos, resultado.Fallidas);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Retención del historial: falló la vuelta; se reintenta mañana.");
        }
    }
}
