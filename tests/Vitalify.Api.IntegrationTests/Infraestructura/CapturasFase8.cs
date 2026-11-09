using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vitalify.Application.Alertas;
using Vitalify.Application.Puertos;

namespace Vitalify.Api.IntegrationTests.Infraestructura;

/// <summary>Un evento de SignalR tal como salió: nombre y su JSON.</summary>
public sealed record EventoTiempoReal(string Evento, string Json);

/// <summary>Registra cada evento de SignalR y lo pasa al adaptador real.</summary>
public sealed class TiempoRealRegistrado : INotificadorTiempoReal
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentQueue<EventoTiempoReal> _eventos = new();

    public INotificadorTiempoReal? Interno { get; set; }

    public IReadOnlyList<EventoTiempoReal> Eventos => _eventos.ToList();

    public IReadOnlyList<EventoTiempoReal> De(string evento) => _eventos.Where(e => e.Evento == evento).ToList();

    public Task<IReadOnlyDictionary<Guid, bool>> AlertaAsync(string evento, AlertaEventoDto alerta, IReadOnlyCollection<Guid> destinatarios, CancellationToken ct = default)
    {
        Registrar(evento, alerta);
        return Interno!.AlertaAsync(evento, alerta, destinatarios, ct);
    }

    public Task RiesgoActualizadoAsync(Guid servicioId, RiesgoActualizadoDto riesgo, CancellationToken ct = default)
    {
        Registrar("riesgoActualizado", riesgo);
        return Interno!.RiesgoActualizadoAsync(servicioId, riesgo, ct);
    }

    public Task SignosActualizadosAsync(Guid servicioId, SignosActualizadosDto signos, CancellationToken ct = default)
    {
        Registrar("signosActualizados", signos);
        return Interno!.SignosActualizadosAsync(servicioId, signos, ct);
    }

    public Task PlanActualizadoAsync(Guid servicioId, PlanActualizadoDto plan, CancellationToken ct = default)
    {
        Registrar("planActualizado", plan);
        return Interno!.PlanActualizadoAsync(servicioId, plan, ct);
    }

    public Task SugerenciasNnnAsync(Guid servicioId, SugerenciasNnnDto sugerencias, CancellationToken ct = default)
    {
        Registrar("sugerenciasNnn", sugerencias);
        return Interno!.SugerenciasNnnAsync(servicioId, sugerencias, ct);
    }

    private void Registrar(string evento, object datos) => _eventos.Enqueue(new EventoTiempoReal(evento, JsonSerializer.Serialize(datos, datos.GetType(), Web)));
}

/// <summary>Proveedor de log que guarda cada mensaje (con su excepción) en memoria.</summary>
public sealed class LogsCapturados : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lineas = new();

    public IReadOnlyList<string> Lineas => _lineas.ToList();

    public int Cantidad => _lineas.Count;

    public ILogger CreateLogger(string categoryName) => new Registrador(categoryName, _lineas);

    public void Dispose()
    {
    }

    private sealed class Registrador(string categoria, ConcurrentQueue<string> lineas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                lineas.Enqueue($"{categoria}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
