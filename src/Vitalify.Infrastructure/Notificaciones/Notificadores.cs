using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Vitalify.Application.Puertos;

namespace Vitalify.Infrastructure.Notificaciones;

/// <summary>Sección <c>Push</c>: <c>Push__Habilitado</c> y <c>Push__RutaCredencialesFirebase</c> (JSON de la cuenta de servicio, fuera del repositorio).</summary>
public sealed record OpcionesPush(bool Habilitado, string? RutaCredencialesFirebase);

/// <summary>
/// Adaptador por defecto (<c>Push__Habilitado=false</c>): escribe en el log lo que se habría enviado. El título y el cuerpo
/// no tienen datos personales (solo la cama y el puntaje), así que pueden ir al log; el token no.
/// </summary>
internal sealed class NotificadorPushEnLog(ILogger<NotificadorPushEnLog> logger) : INotificadorPush
{
    public Task<IReadOnlyList<ResultadoEnvioPush>> EnviarAsync(IReadOnlyList<string> tokens, MensajePush mensaje, CancellationToken ct = default)
    {
        logger.LogInformation(
            "Push (solo log) a {Dispositivos} dispositivos: {Titulo} — {Cuerpo} (alerta {Alerta}, tipo {Tipo}).",
            tokens.Count, mensaje.Titulo, mensaje.Cuerpo, mensaje.Datos.GetValueOrDefault("alertaId"), mensaje.Datos.GetValueOrDefault("tipo"));
        return Task.FromResult<IReadOnlyList<ResultadoEnvioPush>>(tokens.Select(_ => ResultadoEnvioPush.Enviado).ToList());
    }
}

/// <summary>
/// Firebase Cloud Messaging (paquete <c>FirebaseAdmin</c>). Solo con <c>Push__Habilitado=true</c>. Un token que FCM da
/// por no registrado vuelve como <see cref="ResultadoEnvioPush.NoRegistrado"/> para que se borre. Nunca lanza.
/// </summary>
internal sealed class NotificadorPushFcm(OpcionesPush opciones, ILogger<NotificadorPushFcm> logger) : INotificadorPush
{
    private const int MaximoPorEnvio = 500;
    private static readonly object Cerrojo = new();
    private FirebaseMessaging? _mensajeria;

    public async Task<IReadOnlyList<ResultadoEnvioPush>> EnviarAsync(IReadOnlyList<string> tokens, MensajePush mensaje, CancellationToken ct = default)
    {
        var resultados = new List<ResultadoEnvioPush>(tokens.Count);
        try
        {
            var mensajeria = Mensajeria();
            foreach (var lote in tokens.Chunk(MaximoPorEnvio))
            {
                var respuesta = await mensajeria.SendEachAsync(lote.Select(token => new Message
                {
                    // FirebaseAdmin 3.7 recomienda Fid (Firebase Installation ID), pero la app Flutter registra el token de
                    // registro de FCM (FirebaseMessaging.getToken()), que FCM sigue aceptando en Token.
#pragma warning disable CS0618
                    Token = token,
#pragma warning restore CS0618
                    Notification = new Notification { Title = mensaje.Titulo, Body = mensaje.Cuerpo },
                    Data = mensaje.Datos,
                    Android = new AndroidConfig { Priority = Priority.High },
                }), ct);

                resultados.AddRange(respuesta.Responses.Select(r => r.IsSuccess
                    ? ResultadoEnvioPush.Enviado
                    : r.Exception?.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument
                        ? ResultadoEnvioPush.NoRegistrado
                        : ResultadoEnvioPush.Error));
            }

            logger.LogInformation("Push FCM: {Enviados} de {Total} enviados (alerta {Alerta}).",
                resultados.Count(r => r == ResultadoEnvioPush.Enviado), tokens.Count, mensaje.Datos.GetValueOrDefault("alertaId"));
            return resultados;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Push FCM: no se pudo enviar ({Error}).", ex.Message);
            return [.. resultados, .. Enumerable.Repeat(ResultadoEnvioPush.Error, tokens.Count - resultados.Count)];
        }
    }

    private FirebaseMessaging Mensajeria()
    {
        if (_mensajeria is not null)
        {
            return _mensajeria;
        }

        lock (Cerrojo)
        {
            var app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
            {
                Credential = CredentialFactory.FromFile<ServiceAccountCredential>(opciones.RutaCredencialesFirebase!).ToGoogleCredential(),
            });
            return _mensajeria = FirebaseMessaging.GetMessaging(app);
        }
    }
}

/// <summary>Escribe en el log los fallos que no interrumpen el flujo (solo ids en el texto).</summary>
internal sealed class BitacoraFallosEnLog(ILogger<BitacoraFallosEnLog> logger) : IBitacoraFallos
{
    public void Registrar(string operacion, Exception error) => logger.LogError(error, "Falló sin interrumpir el flujo: {Operacion}.", operacion);
}
