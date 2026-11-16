using Vitalify.Application.Alertas;
using Vitalify.Application.Nnn;

namespace Vitalify.Api.Entrada.Alertas;

/// <summary>
/// Cada <see cref="OpcionesAlertas.IntervaloVigilancia"/> (30 s) llama a <see cref="VigilarAlertas"/>: sin señal, tomas
/// vencidas, escalamiento y cambio de turno; y después a <see cref="VigilarPlanCuidados"/> (fase 9): sugerencias que ya
/// persistieron, recordatorios del plan y el aviso de cierre de guardia. Cada uno en su propio intento: un fallo del plan no
/// detiene las alertas. Con el intervalo en 0 (<c>Alertas__SegundosVigilancia=0</c>) no hace nada.
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
        DateTime? ultimaRevisionPlan = null;
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

                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var reloj = scope.ServiceProvider.GetRequiredService<Application.Puertos.IReloj>();
                    var inicio = reloj.AhoraUtc;
                    var resumen = await scope.ServiceProvider.GetRequiredService<VigilarPlanCuidados>().EjecutarAsync(ultimaRevisionPlan, stoppingToken);
                    ultimaRevisionPlan = inicio;
                    if (resumen is { SugerenciasActualizadas: > 0 } or { PushEnviados: > 0 })
                    {
                        logger.LogInformation(
                            "Vigilante del plan de cuidados: {Sugerencias} pacientes con sugerencias nuevas o expiradas, {Vencidas} con intervenciones vencidas y {Push} push enviados.",
                            resumen.SugerenciasActualizadas, resumen.PacientesConVencidas, resumen.PushEnviados);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Vigilante del plan de cuidados: falló la vuelta; se reintenta en el siguiente ciclo.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La API se está deteniendo.
        }
    }
}
