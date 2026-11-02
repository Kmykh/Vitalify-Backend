using Vitalify.Application.Vinculacion;

namespace Vitalify.Api.Entrada.Vinculacion;

/// <summary>
/// Cada <see cref="OpcionesVinculacion.IntervaloRenovacion"/> (60 s) publica un QR nuevo a los wearables en línea y sin
/// vincular cuyo código venció. Solo llama al caso de uso: no toca la base directamente. Con el intervalo en 0
/// (<c>Vinculacion__SegundosRenovacion=0</c>) no hace nada.
/// </summary>
public sealed class RenovadorCodigosVinculacionWorker(
    OpcionesVinculacion opciones, IServiceScopeFactory scopes, ILogger<RenovadorCodigosVinculacionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (opciones.IntervaloRenovacion <= TimeSpan.Zero)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(opciones.IntervaloRenovacion);
        try
        {
            while (await temporizador.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var renovados = await scope.ServiceProvider.GetRequiredService<RenovarCodigosVinculacion>().EjecutarAsync(stoppingToken);
                    if (renovados > 0)
                    {
                        logger.LogInformation("Vinculación: se renovaron {Cantidad} códigos QR vencidos.", renovados);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Vinculación: no se pudieron renovar los códigos QR; se reintenta en el siguiente ciclo.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // La API se está deteniendo.
        }
    }
}
