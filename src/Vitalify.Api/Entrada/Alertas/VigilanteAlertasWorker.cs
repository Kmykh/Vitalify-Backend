using Vitalify.Application.Alertas;

namespace Vitalify.Api.Entrada.Alertas;

/// <summary>
/// Cada <see cref="OpcionesAlertas.IntervaloVigilancia"/> (30 s) llama a <see cref="VigilarAlertas"/>: sin señal, tomas
/// vencidas, escalamiento y cambio de turno. Con el intervalo en 0 (<c>Alertas__SegundosVigilancia=0</c>) no hace nada.
/// </summary>
public sealed class VigilanteAlertasWorker(OpcionesAlertas opciones, IServiceScopeFactory scopes, ILogger<VigilanteAlertasWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (opciones.IntervaloVigilancia <= TimeSpan.Zero)
        {
            return;
        }

        DateTime? ultimaRevision = null;
        using var temporizador = new PeriodicTimer(opciones.IntervaloVigilancia);
        try
        {
            while (await temporizador.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var reloj = scope.ServiceProvider.GetRequiredService<Application.Puertos.IReloj>();
                    var inicio = reloj.AhoraUtc;
                    var resumen = await scope.ServiceProvider.GetRequiredService<VigilarAlertas>().EjecutarAsync(ultimaRevision, stoppingToken);
                    ultimaRevision = inicio;
                    if (resumen is { SinSenal: > 0 } or { TomasPendientes: > 0 } or { Escaladas: > 0 } or { AvisosCambioDeTurno: > 0 })
                    {
                        logger.LogInformation(
                            "Vigilante de alertas: {SinSenal} sin señal, {Tomas} tomas vencidas, {Escaladas} escaladas y {Avisos} avisos de cambio de turno.",
                            resumen.SinSenal, resumen.TomasPendientes, resumen.Escaladas, resumen.AvisosCambioDeTurno);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Vigilante de alertas: falló la vuelta; se reintenta en el siguiente ciclo.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La API se está deteniendo.
        }
    }
}
