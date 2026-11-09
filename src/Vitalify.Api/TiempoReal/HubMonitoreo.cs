using Microsoft.AspNetCore.Http.Connections;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Vitalify.Api.Seguridad;
using Vitalify.Application.Alertas;
using Vitalify.Application.Puertos;
using Vitalify.Application.Usuarios;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Api.TiempoReal;

/// <summary>Nombres de los grupos de SignalR.</summary>
public static class GruposMonitoreo
{
    public static string Servicio(Guid servicioId) => $"servicio:{servicioId}";

    public static string Usuario(Guid usuarioId) => $"usuario:{usuarioId}";

    public static string Medicos(Guid servicioId) => $"medicos:{servicioId}";
}

/// <summary>
/// Hub de monitoreo (<c>/hubs/monitoreo</c>), con el mismo JWT de la API (en el query string <c>access_token</c>, solo
/// para esta ruta). Al conectarse, el usuario entra a <c>usuario:{id}</c>, a <c>servicio:{id}</c> de su servicio y, si es
/// médico, a <c>medicos:{servicioId}</c>. Eventos: <c>alertaCreada</c>, <c>alertaActualizada</c>, <c>riesgoActualizado</c>
/// y <c>signosActualizados</c>. No lleva nombres de pacientes: el cliente los pide a la API con su permiso.
/// </summary>
[Authorize(Policy = Politicas.PersonalClinico)]
public sealed class HubMonitoreo(ObtenerUsuarioActual obtenerUsuario, ConexionesMonitoreo conexiones, ILogger<HubMonitoreo> logger) : Hub
{
    public const string Ruta = "/hubs/monitoreo";

    public override async Task OnConnectedAsync()
    {
        var usuarioId = Context.User?.IdUsuario() ?? Guid.Empty;
        var usuario = await obtenerUsuario.EjecutarAsync(usuarioId, Context.ConnectionAborted);
        if (!usuario.EsExito)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GruposMonitoreo.Usuario(usuarioId));
        if (usuario.Valor.ServicioId is { } servicioId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GruposMonitoreo.Servicio(servicioId));
            if (usuario.Valor.Rol == nameof(Rol.Medico))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, GruposMonitoreo.Medicos(servicioId));
            }
        }
        else
        {
            logger.LogWarning("SignalR: el usuario {Usuario} no tiene servicio asignado; solo recibirá eventos de su grupo personal.", usuarioId);
        }

        conexiones.Conectar(usuarioId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.User?.IdUsuario() is { } usuarioId)
        {
            conexiones.Desconectar(usuarioId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

/// <summary>Cuántas conexiones abiertas tiene cada usuario (en esta instancia), para registrar si la notificación le llegó.</summary>
public sealed class ConexionesMonitoreo
{
    private readonly ConcurrentDictionary<Guid, int> _conexiones = new();

    public void Conectar(Guid usuarioId) => _conexiones.AddOrUpdate(usuarioId, 1, (_, n) => n + 1);

    public void Desconectar(Guid usuarioId) => _conexiones.AddOrUpdate(usuarioId, 0, (_, n) => Math.Max(0, n - 1));

    public bool EstaConectado(Guid usuarioId) => _conexiones.TryGetValue(usuarioId, out var n) && n > 0;
}

/// <summary>Adaptador de <see cref="INotificadorTiempoReal"/> con SignalR: los eventos van al grupo del servicio. Nunca lanza.</summary>
public sealed class NotificadorSignalR(IHubContext<HubMonitoreo> hub, ConexionesMonitoreo conexiones, ILogger<NotificadorSignalR> logger)
    : INotificadorTiempoReal
{
    public async Task<IReadOnlyDictionary<Guid, bool>> AlertaAsync(
        string evento, AlertaEventoDto alerta, IReadOnlyCollection<Guid> destinatarios, CancellationToken ct = default)
    {
        await EnviarAsync(GruposMonitoreo.Servicio(alerta.ServicioId), evento, alerta, ct);
        return destinatarios.Distinct().ToDictionary(d => d, conexiones.EstaConectado);
    }

    public Task RiesgoActualizadoAsync(Guid servicioId, RiesgoActualizadoDto riesgo, CancellationToken ct = default) =>
        EnviarAsync(GruposMonitoreo.Servicio(servicioId), "riesgoActualizado", riesgo, ct);

    public Task SignosActualizadosAsync(Guid servicioId, SignosActualizadosDto signos, CancellationToken ct = default) =>
        EnviarAsync(GruposMonitoreo.Servicio(servicioId), "signosActualizados", signos, ct);

    public Task PlanActualizadoAsync(Guid servicioId, PlanActualizadoDto plan, CancellationToken ct = default) =>
        EnviarAsync(GruposMonitoreo.Servicio(servicioId), "planActualizado", plan, ct);

    public Task SugerenciasNnnAsync(Guid servicioId, SugerenciasNnnDto sugerencias, CancellationToken ct = default) =>
        EnviarAsync(GruposMonitoreo.Servicio(servicioId), "sugerenciasNnn", sugerencias, ct);

    private async Task EnviarAsync(string grupo, string evento, object datos, CancellationToken ct)
    {
        try
        {
            await hub.Clients.Group(grupo).SendAsync(evento, datos, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("SignalR: no se pudo enviar {Evento} al grupo {Grupo} ({Error}).", evento, grupo, ex.Message);
        }
    }
}

internal static class RegistroTiempoReal
{
    /// <summary>
    /// Ping del servidor cada 15 s y desconexión si el cliente calla 30 s: detrás del proxy (Caddy) una conexión
    /// WebSocket sin tráfico no se corta, y un cliente que perdió la red se detecta pronto. El cliente debe usar un
    /// <c>serverTimeout</c> de al menos 30 s (el doble del KeepAlive).
    /// </summary>
    public static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan TiempoMaximoCliente = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddTiempoReal(this IServiceCollection services)
    {
        services.AddSignalR(o =>
        {
            o.KeepAliveInterval = KeepAlive;
            o.ClientTimeoutInterval = TiempoMaximoCliente;
        });
        services.AddSingleton<ConexionesMonitoreo>();
        services.AddSingleton<INotificadorTiempoReal, NotificadorSignalR>();
        return services;
    }

    /// <summary>
    /// El hub con WebSockets (lo preferido; Caddy los reenvía sin configuración) y, si un proxy o red los bloquea, Server-Sent
    /// Events o long polling. Con la política CORS que admite credenciales, que solo usa el hub.
    /// </summary>
    public static IEndpointConventionBuilder MapTiempoReal(this IEndpointRouteBuilder app) =>
        app.MapHub<HubMonitoreo>(HubMonitoreo.Ruta, o =>
            o.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling)
            .RequireCors(Configuracion.CorsVitalify.PoliticaSignalR);
}
