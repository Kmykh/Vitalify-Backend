using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;

namespace Vitalify.Api.Entrada.Simulador;

/// <summary>
/// Adaptador de entrada temporal: cada <see cref="OpcionesSimulador.IntervaloSegundos"/> genera una lectura por cada
/// dispositivo vinculado a una hospitalización activa y la pasa a <see cref="IRegistrarLectura"/>, el mismo puerto que
/// usará MQTT en la fase 7. Nunca escribe en la base directamente. Si una lectura falla, lo registra y sigue.
/// </summary>
public sealed class SimuladorTelemetriaWorker(
    IServiceScopeFactory scopes,
    OpcionesSimulador opciones,
    GeneradorLecturasSimuladas generador,
    IReloj reloj,
    ILogger<SimuladorTelemetriaWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opciones.Habilitado)
        {
            logger.LogInformation("Simulador de telemetría deshabilitado (Simulador__Habilitado=false).");
            return;
        }

        var intervalo = Math.Max(OpcionesSimulador.IntervaloMinimoSegundos, opciones.IntervaloSegundos);
        if (intervalo != opciones.IntervaloSegundos)
        {
            logger.LogWarning("Simulador__IntervaloSegundos={Configurado} es menor que el mínimo; se usan {Intervalo} s.", opciones.IntervaloSegundos, intervalo);
        }

        logger.LogInformation("Simulador de telemetría habilitado: una lectura cada {Intervalo} s por dispositivo (semilla {Semilla}).", intervalo, opciones.Semilla);

        using var temporizador = new PeriodicTimer(TimeSpan.FromSeconds(intervalo));
        do
        {
            try
            {
                await EjecutarCicloAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falló un ciclo del simulador de telemetría; se reintenta en el siguiente.");
            }
        }
        while (await temporizador.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Un ciclo: una lectura por dispositivo vinculado. Devuelve cuántas se procesaron sin error.</summary>
    public async Task<int> EjecutarCicloAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var dispositivos = await scope.ServiceProvider.GetRequiredService<IListarDispositivosVinculados>().EjecutarAsync(ct);
        var procesadas = 0;

        foreach (var codigo in dispositivos)
        {
            try
            {
                // Un scope por lectura: si una falla, su contexto de EF no contamina a las siguientes.
                await using var scopeLectura = scopes.CreateAsyncScope();
                var registrar = scopeLectura.ServiceProvider.GetRequiredService<IRegistrarLectura>();
                var resultado = await registrar.EjecutarAsync(generador.Generar(codigo, reloj.AhoraUtc), ct);

                if (resultado.EsExito)
                {
                    procesadas++;
                    logger.LogDebug("Simulador: {Dispositivo} -> {Estado}", codigo, resultado.Valor.Estado);
                }
                else
                {
                    logger.LogWarning("Simulador: {Dispositivo} no se registró ({Codigo}).", codigo, resultado.Error.Codigo);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Simulador: error al registrar la lectura de {Dispositivo}.", codigo);
            }
        }

        return procesadas;
    }
}

internal static class RegistroSimulador
{
    public static IServiceCollection AddSimuladorTelemetria(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(OpcionesSimulador.Cargar(configuration));
        services.AddSingleton<GeneradorLecturasSimuladas>();
        services.AddHostedService<SimuladorTelemetriaWorker>();
        return services;
    }
}
