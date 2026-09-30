using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Camas;
using Vitalify.Domain.Dispositivos;

namespace Vitalify.Infrastructure.Semillas;

/// <summary>
/// Solo en Development y con <c>Seed__DatosDemo=true</c>: crea las camas <c>MED-B-01</c> a <c>MED-B-06</c>
/// (servicio "Medicina B") y los dispositivos <c>ESP32-001</c> a <c>ESP32-004</c> que no existan. No crea pacientes.
/// </summary>
internal sealed class SemillaDatosDemo(
    IServiceScopeFactory scopes, IConfiguration configuration, IHostEnvironment entorno, ILogger<SemillaDatosDemo> logger)
    : IHostedService
{
    public const string ServicioDemo = "Medicina B";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!bool.TryParse(configuration["Seed:DatosDemo"], out var activa) || !activa)
        {
            return;
        }

        if (!entorno.IsDevelopment())
        {
            logger.LogWarning("Seed__DatosDemo=true se ignora fuera de Development.");
            return;
        }

        try
        {
            await CrearAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudieron crear los datos de demostración.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CrearAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var servicios = scope.ServiceProvider;
        var camas = servicios.GetRequiredService<IRepositorioCamas>();
        var dispositivos = servicios.GetRequiredService<IRepositorioDispositivos>();
        var camasCreadas = 0;
        var dispositivosCreados = 0;

        foreach (var codigo in Enumerable.Range(1, 6).Select(n => $"MED-B-{n:00}"))
        {
            if (!await camas.ExisteCodigoAsync(codigo, ct))
            {
                camas.Agregar(Cama.Registrar(codigo, ServicioDemo));
                camasCreadas++;
            }
        }

        foreach (var codigo in Enumerable.Range(1, 4).Select(n => $"ESP32-{n:000}"))
        {
            if (!await dispositivos.ExisteCodigoAsync(codigo, ct))
            {
                dispositivos.Agregar(Dispositivo.Registrar(codigo, "Wearable de demostración"));
                dispositivosCreados++;
            }
        }

        var guardado = await servicios.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync(ct);
        if (guardado.EsExito)
        {
            logger.LogInformation("Datos de demostración: {Camas} camas y {Dispositivos} dispositivos creados.", camasCreadas, dispositivosCreados);
        }
        else
        {
            logger.LogWarning("No se crearon los datos de demostración: {Motivo}", guardado.Error.Mensaje);
        }
    }
}
