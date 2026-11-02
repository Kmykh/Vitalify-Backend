using Vitalify.Application.Comun;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Application.Vinculacion;

/// <summary>
/// Qué debe mostrar el wearable cuando se conecta o pide un QR:
/// <list type="bullet">
/// <item>vinculado → se vuelve a publicar <c>vinculado</c> (por si el ESP32 se reinició);</item>
/// <item>disponible con un escaneo en curso → <c>emparejando</c>, para no romper la vinculación de la enfermera;</item>
/// <item>disponible → un QR nuevo (<c>mostrar-qr</c>);</item>
/// <item>en mantenimiento, dado de baja o desconocido → nada.</item>
/// </list>
/// </summary>
public sealed class SincronizarPantallaDispositivo(
    IRepositorioDispositivos dispositivos,
    IRepositorioCodigosVinculacion codigos,
    ServicioCodigosVinculacion servicio,
    IPublicadorComandosDispositivo publicador,
    IReloj reloj)
{
    public const string Vinculado = "vinculado";
    public const string Emparejando = "emparejando";
    public const string QrPublicado = "qr";
    public const string SinCambios = "sin-cambios";

    public async Task<string> EjecutarAsync(string codigoDispositivo, string motivo, CancellationToken ct = default)
    {
        var dispositivo = await dispositivos.ObtenerPorCodigoAsync(Dispositivo.NormalizarCodigo(codigoDispositivo), ct);
        if (dispositivo is null)
        {
            return SinCambios;
        }

        if (dispositivo.Estado == EstadoDispositivo.Asignado)
        {
            await servicio.PublicarVinculadoAsync(dispositivo, ct);
            return Vinculado;
        }

        if (!dispositivo.PuedeAsignarse)
        {
            return SinCambios;
        }

        if ((await codigos.ObtenerActivoAsync(dispositivo.Id, ct))?.TieneTokenVigente(reloj.AhoraUtc) == true)
        {
            await publicador.PublicarAsync(dispositivo.Codigo, new Emparejando(), retenido: false, ct);
            return Emparejando;
        }

        var generado = await servicio.GenerarYPublicarAsync(dispositivo, motivo, null, null, ct);
        return generado.EsExito ? QrPublicado : SinCambios;
    }
}

/// <summary>
/// Registra el estado de conexión que publica cada wearable en <c>device/{codigo}/estado</c> (lo llama el adaptador
/// MQTT). Al pasar a <c>online</c>, sincroniza su pantalla: QR si no está vinculado, <c>vinculado</c> si lo está.
/// </summary>
public sealed class RegistrarPresenciaDispositivo(IPresenciaDispositivos presencia, SincronizarPantallaDispositivo sincronizar, IReloj reloj)
{
    public const string EnLinea = "en-linea";
    public const string FueraDeLinea = "fuera-de-linea";
    public const string Desconocida = "desconocida";

    /// <returns>false si el texto no es <c>online</c> ni <c>offline</c>.</returns>
    public async Task<bool> EjecutarAsync(string codigoDispositivo, string estado, CancellationToken ct = default)
    {
        var texto = estado.Trim().ToLowerInvariant();
        if (texto is not ("online" or "offline"))
        {
            return false;
        }

        var codigo = Dispositivo.NormalizarCodigo(codigoDispositivo);
        var enLinea = texto == "online";
        presencia.Registrar(codigo, new PresenciaDispositivo(enLinea, reloj.AhoraUtc));
        if (enLinea)
        {
            await sincronizar.EjecutarAsync(codigo, "en línea", ct);
        }

        return true;
    }

    public static string Texto(PresenciaDispositivo? presencia) =>
        presencia is null ? Desconocida : presencia.EnLinea ? EnLinea : FueraDeLinea;
}

/// <summary>Lo que pide el wearable por <c>device/{codigo}/solicitudes</c>: hoy solo <c>{"accion":"solicitar-qr"}</c>.</summary>
public sealed class AtenderSolicitudDispositivo(SincronizarPantallaDispositivo sincronizar)
{
    /// <returns>Lo que se publicó, o null si la solicitud no es del contrato.</returns>
    public async Task<string?> EjecutarAsync(string codigoDispositivo, ReadOnlyMemory<byte> payload, CancellationToken ct = default) =>
        ContratoComandos.EsSolicitudQr(payload.Span)
            ? await sincronizar.EjecutarAsync(codigoDispositivo, "solicitado por el dispositivo", ct)
            : null;
}

public sealed record RegenerarQrComando(Guid DispositivoId, Guid UsuarioId, string? Ip);

/// <summary>Un usuario pide un QR nuevo para un sensor disponible (por ejemplo, si el que muestra la pantalla venció).</summary>
public sealed class RegenerarQrDispositivo(IRepositorioDispositivos dispositivos, ServicioCodigosVinculacion servicio)
{
    public async Task<Resultado<QrGeneradoDto>> EjecutarAsync(RegenerarQrComando comando, CancellationToken ct = default) =>
        await dispositivos.ObtenerPorIdAsync(comando.DispositivoId, ct) is { } dispositivo
            ? await servicio.GenerarYPublicarAsync(dispositivo, "solicitado por un usuario", comando.UsuarioId, comando.Ip, ct)
            : ErroresDispositivo.NoEncontrado;
}

public sealed record EscanearQrComando(string? Qr, Guid UsuarioId, string? Ip);

/// <summary>
/// La enfermera escanea el QR que muestra el wearable. Si el código es el activo del dispositivo, está vigente y el
/// sensor está en línea, se emite un token de vinculación y el wearable pasa a "emparejando". Un QR vencido, ya
/// usado o inventado responde siempre lo mismo (404). Al quinto código equivocado sobre un mismo dispositivo, su QR
/// se invalida y se publica uno nuevo.
/// </summary>
public sealed class EscanearQr(
    IRepositorioDispositivos dispositivos,
    IRepositorioCodigosVinculacion codigos,
    IPresenciaDispositivos presencia,
    ServicioCodigosVinculacion servicio,
    IPublicadorComandosDispositivo publicador,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    OpcionesVinculacion opciones,
    IReloj reloj)
{
    public const string ConexionEstablecida = "establecida";

    public async Task<Resultado<EscaneoQrDto>> EjecutarAsync(EscanearQrComando comando, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comando.Qr))
        {
            return ErroresComunes.ValidacionDeCampo("qr", "El contenido del QR es obligatorio.");
        }

        if (!FormatoQr.TryInterpretar(comando.Qr, out var codigoDispositivo, out var codigoQr))
        {
            return await RechazarAsync(null, "formato no válido", comando, ct);
        }

        var dispositivo = await dispositivos.ObtenerPorCodigoAsync(codigoDispositivo, ct);
        if (dispositivo is null)
        {
            return await RechazarAsync(null, "dispositivo desconocido", comando, ct);
        }

        var ahora = reloj.AhoraUtc;
        var activo = await codigos.ObtenerActivoAsync(dispositivo.Id, ct);
        if (activo is null)
        {
            return await RechazarAsync(dispositivo, "sin código activo", comando, ct);
        }

        if (!activo.Coincide(codigoQr))
        {
            var agotado = activo.RegistrarIntentoFallido(ahora, opciones.IntentosMaximos);
            var rechazo = await RechazarAsync(dispositivo, agotado ? "código incorrecto; intentos agotados" : "código incorrecto", comando, ct);
            if (agotado)
            {
                await servicio.GenerarYPublicarAsync(dispositivo, "intentos fallidos agotados", null, null, ct);
            }

            return rechazo;
        }

        if (!activo.EstaVigente(ahora))
        {
            return await RechazarAsync(dispositivo, "código vencido", comando, ct);
        }

        if (presencia.Obtener(dispositivo.Codigo) is not { EnLinea: true })
        {
            await RechazarAsync(dispositivo, "sensor sin conexión", comando, ct);
            return ErroresVinculacion.SensorSinConexion;
        }

        var token = activo.EmitirToken(ahora, opciones.VigenciaToken);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.QrEscaneado, ahora, comando.UsuarioId, comando.Ip, $"Código {activo.Id} del dispositivo {dispositivo.Id}."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        await publicador.PublicarAsync(dispositivo.Codigo, new Emparejando(), retenido: false, ct);
        return new EscaneoQrDto(token, new DispositivoEscaneadoDto(dispositivo.Id, dispositivo.Codigo), ConexionEstablecida, activo.TokenExpiraEn!.Value);
    }

    private async Task<Error> RechazarAsync(Dispositivo? dispositivo, string motivo, EscanearQrComando comando, CancellationToken ct)
    {
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.QrRechazado, reloj.AhoraUtc, comando.UsuarioId, comando.Ip,
            dispositivo is null ? $"QR rechazado ({motivo})." : $"QR del dispositivo {dispositivo.Id} rechazado ({motivo})."));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return ErroresVinculacion.QrNoValido;
    }
}

/// <summary>
/// Renueva el QR de los wearables en línea y sin vincular cuyo código venció (o que no tienen ninguno). No toca un
/// código con un escaneo en curso. Lo llama un worker cada <see cref="OpcionesVinculacion.IntervaloRenovacion"/>.
/// </summary>
public sealed class RenovarCodigosVinculacion(
    IPresenciaDispositivos presencia,
    IRepositorioDispositivos dispositivos,
    IRepositorioCodigosVinculacion codigos,
    ServicioCodigosVinculacion servicio,
    IReloj reloj)
{
    /// <returns>Cuántos QR nuevos se publicaron.</returns>
    public async Task<int> EjecutarAsync(CancellationToken ct = default)
    {
        var renovados = 0;
        foreach (var codigoDispositivo in presencia.ListarEnLinea())
        {
            var dispositivo = await dispositivos.ObtenerPorCodigoAsync(codigoDispositivo, ct);
            if (dispositivo is not { PuedeAsignarse: true })
            {
                continue;
            }

            var ahora = reloj.AhoraUtc;
            var activo = await codigos.ObtenerActivoAsync(dispositivo.Id, ct);
            if (activo is not null && (activo.EstaVigente(ahora) || activo.TieneTokenVigente(ahora)))
            {
                continue;
            }

            if ((await servicio.GenerarYPublicarAsync(dispositivo, "renovación", null, null, ct)).EsExito)
            {
                renovados++;
            }
        }

        return renovados;
    }
}
