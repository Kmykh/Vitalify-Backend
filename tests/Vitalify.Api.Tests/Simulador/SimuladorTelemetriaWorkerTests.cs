using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vitalify.Api.Entrada.Simulador;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Api.Tests.Simulador;

public class SimuladorTelemetriaWorkerTests
{
    private readonly RegistrarLecturaFalso _registrar = new();
    private readonly RelojFijo _reloj = new();

    private SimuladorTelemetriaWorker Worker(params string[] dispositivos)
    {
        // Sin DbContext ni repositorios: si el worker intentara escribir en la base, no tendría cómo.
        var servicios = new ServiceCollection()
            .AddSingleton<IRegistrarLectura>(_registrar)
            .AddSingleton<IListarDispositivosVinculados>(new DispositivosFijos(dispositivos))
            .BuildServiceProvider();
        var opciones = new OpcionesSimulador { Habilitado = true, Semilla = 3 };

        return new SimuladorTelemetriaWorker(
            servicios.GetRequiredService<IServiceScopeFactory>(), opciones, new GeneradorLecturasSimuladas(opciones), _reloj,
            NullLogger<SimuladorTelemetriaWorker>.Instance);
    }

    [Fact]
    public async Task Cada_ciclo_llama_al_puerto_RegistrarLectura_una_vez_por_dispositivo_vinculado()
    {
        var worker = Worker("ESP32-001", "ESP32-002");

        var procesadas = await worker.EjecutarCicloAsync();
        _reloj.AhoraUtc = _reloj.AhoraUtc.AddSeconds(10);
        await worker.EjecutarCicloAsync();

        Assert.Equal(2, procesadas);
        Assert.Equal(["ESP32-001", "ESP32-002", "ESP32-001", "ESP32-002"], _registrar.Recibidas.Select(l => l.CodigoDispositivo));
        Assert.All(_registrar.Recibidas, l => Assert.Equal(OrigenLectura.Simulador, l.Origen));
        Assert.Equal([1L, 2L], _registrar.Recibidas.Where(l => l.CodigoDispositivo == "ESP32-001").Select(l => l.Seq));
    }

    [Fact]
    public async Task Si_una_lectura_falla_el_ciclo_sigue_con_las_demas()
    {
        _registrar.FallarCon = "ESP32-001";
        var worker = Worker("ESP32-001", "ESP32-002");

        var procesadas = await worker.EjecutarCicloAsync();

        Assert.Equal(1, procesadas);
        Assert.Equal("ESP32-002", Assert.Single(_registrar.Recibidas).CodigoDispositivo);
    }

    [Fact]
    public async Task Sin_dispositivos_vinculados_no_envia_nada()
    {
        Assert.Equal(0, await Worker().EjecutarCicloAsync());
        Assert.Empty(_registrar.Recibidas);
    }

    private sealed class RegistrarLecturaFalso : IRegistrarLectura
    {
        public List<LecturaTelemetria> Recibidas { get; } = [];

        public string? FallarCon { get; set; }

        public Task<Resultado<ResultadoIngesta>> EjecutarAsync(LecturaTelemetria lectura, CancellationToken ct = default)
        {
            if (lectura.CodigoDispositivo == FallarCon)
            {
                throw new InvalidOperationException("Fallo simulado de la base de datos.");
            }

            Recibidas.Add(lectura);
            return Task.FromResult<Resultado<ResultadoIngesta>>(ResultadoIngesta.De(EstadoIngesta.Aceptada, lectura, []));
        }
    }

    private sealed class DispositivosFijos(IReadOnlyList<string> codigos) : IListarDispositivosVinculados
    {
        public Task<IReadOnlyList<string>> EjecutarAsync(CancellationToken ct = default) => Task.FromResult(codigos);
    }

    private sealed class RelojFijo : IReloj
    {
        public DateTime AhoraUtc { get; set; } = new(2026, 9, 30, 14, 0, 0, DateTimeKind.Utc);
    }
}
