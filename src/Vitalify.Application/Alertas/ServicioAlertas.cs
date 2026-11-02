using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Alertas;

/// <summary>Contexto de cada hospitalización, recordado durante la petición (o el ciclo del worker).</summary>
public sealed class ContextoHospitalizaciones(IRepositorioHospitalizaciones hospitalizaciones)
{
    private readonly Dictionary<Guid, ContextoHospitalizacion?> _cache = [];

    public async Task<ContextoHospitalizacion?> ObtenerAsync(Guid hospitalizacionId, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(hospitalizacionId, out var contexto))
        {
            contexto = await hospitalizaciones.ObtenerContextoAsync(hospitalizacionId, ct);
            _cache[hospitalizacionId] = contexto;
        }

        return contexto;
    }
}

/// <summary>A quién se le pide atención por una alerta (sección 2.2 y 2.3 de la fase 6).</summary>
public sealed class DestinatariosAlertas(ITurnoVigente turno, IRepositorioUsuarios usuarios, IReloj reloj)
{
    public async Task<IReadOnlyList<Guid>> ParaAsync(AlertaClinica alerta, string motivo, CancellationToken ct = default)
    {
        if (motivo == MotivosNotificacion.Escalamiento)
        {
            var medicos = await MedicosAsync(alerta.ServicioId, ct);
            return alerta.Prioridad == PrioridadAlerta.Alta
                ? [.. medicos, .. (await usuarios.ListarActivosAsync(Rol.Enfermera, alerta.ServicioId, ct)).Select(u => u.Id).Except(medicos)]
                : medicos;
        }

        if (alerta.Tipo == TipoAlerta.PreocupacionClinica)
        {
            return await MedicosAsync(alerta.ServicioId, ct);
        }

        var deTurno = (await turno.EnfermerasDeTurnoAsync(alerta.ServicioId, reloj.AhoraUtc, ct)).Enfermeras;
        return alerta.Tipo == TipoAlerta.Riesgo && alerta.Prioridad == PrioridadAlerta.Alta
            ? [.. deTurno, .. (await MedicosAsync(alerta.ServicioId, ct)).Except(deTurno)]
            : deTurno;
    }

    /// <summary>Médicos activos del servicio; si el servicio no tiene ninguno, todos los médicos activos.</summary>
    public async Task<IReadOnlyList<Guid>> MedicosAsync(Guid servicioId, CancellationToken ct = default)
    {
        var delServicio = await usuarios.ListarActivosAsync(Rol.Medico, servicioId, ct);
        return (delServicio.Count > 0 ? delServicio : await usuarios.ListarActivosAsync(Rol.Medico, null, ct)).Select(u => u.Id).ToList();
    }
}

/// <summary>
/// Notifica una alerta por SignalR (al grupo del servicio) y por push (a cada destinatario), y deja cada envío en
/// <c>notificada_a</c> (usuario, canal, motivo, momento y resultado). Un fallo al notificar nunca rompe el flujo: queda
/// en la bitácora y en el registro como <c>error</c>.
/// </summary>
public sealed class NotificadorAlertas(
    INotificadorTiempoReal tiempoReal,
    INotificadorPush push,
    IRepositorioDispositivosMoviles moviles,
    IUnidadDeTrabajo unidadDeTrabajo,
    IBitacoraFallos bitacora,
    IReloj reloj)
{
    public const string CanalTiempoReal = "tiempo-real";
    public const string CanalPush = "push";

    public async Task NotificarAsync(AlertaClinica alerta, string cama, string motivo, IReadOnlyCollection<Guid> destinatarios, CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var registros = new List<NotificacionAlerta>();
        var evento = AlertaEventoDto.Desde(alerta, cama, motivo, destinatarios);

        try
        {
            var conectados = await tiempoReal.AlertaAsync(motivo == MotivosNotificacion.Nueva ? "alertaCreada" : "alertaActualizada", evento, destinatarios, ct);
            registros.AddRange(destinatarios.Select(d =>
                new NotificacionAlerta(d, CanalTiempoReal, motivo, ahora, conectados.GetValueOrDefault(d) ? "conectado" : "sin-conexion")));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            bitacora.Registrar($"Tiempo real de la alerta {alerta.Id}", ex);
            registros.AddRange(destinatarios.Select(d => new NotificacionAlerta(d, CanalTiempoReal, motivo, ahora, "error")));
        }

        try
        {
            var dispositivos = destinatarios.Count == 0 ? [] : await moviles.ListarDeUsuariosAsync(destinatarios, ct);
            var mensaje = new MensajePush(TextosPush.Titulo(cama), TextosPush.Cuerpo(alerta, motivo), TextosPush.Datos(alerta));
            var resultados = dispositivos.Count == 0 ? [] : await push.EnviarAsync(dispositivos.Select(d => d.Token).ToList(), mensaje, ct);

            for (var i = 0; i < dispositivos.Count; i++)
            {
                var resultado = i < resultados.Count ? resultados[i] : ResultadoEnvioPush.Error;
                registros.Add(new NotificacionAlerta(dispositivos[i].UsuarioId, CanalPush, motivo, ahora, resultado switch
                {
                    ResultadoEnvioPush.Enviado => "enviado",
                    ResultadoEnvioPush.NoRegistrado => "no-registrado",
                    _ => "error",
                }));

                if (resultado == ResultadoEnvioPush.NoRegistrado)
                {
                    moviles.Eliminar(dispositivos[i]);
                }
            }

            registros.AddRange(destinatarios
                .Where(d => dispositivos.All(m => m.UsuarioId != d))
                .Select(d => new NotificacionAlerta(d, CanalPush, motivo, ahora, "sin-dispositivo")));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            bitacora.Registrar($"Push de la alerta {alerta.Id}", ex);
            registros.AddRange(destinatarios.Select(d => new NotificacionAlerta(d, CanalPush, motivo, ahora, "error")));
        }

        alerta.RegistrarNotificaciones(registros);
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            bitacora.Registrar($"Registro de notificaciones de la alerta {alerta.Id}", new InvalidOperationException(guardado.Error.Codigo));
        }
    }

    /// <summary>Solo actualiza las pantallas del servicio (sin push ni registro): tomada, atendida, resuelta o valores nuevos.</summary>
    public async Task SincronizarAsync(AlertaClinica alerta, string cama, CancellationToken ct = default)
    {
        try
        {
            await tiempoReal.AlertaAsync("alertaActualizada", AlertaEventoDto.Desde(alerta, cama, MotivosNotificacion.Estado, []), [], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            bitacora.Registrar($"Tiempo real de la alerta {alerta.Id}", ex);
        }
    }
}

/// <summary>Un disparador de alerta (lectura, evaluación, observación o vigilante).</summary>
public sealed record DisparoAlerta(
    Guid HospitalizacionId, TipoAlerta Tipo, PrioridadAlerta Prioridad, string? NivelRiesgo = null, int? News2 = null, int? Mews = null,
    IReadOnlyDictionary<string, string>? Detalle = null);

/// <summary>
/// Crea o actualiza alertas. Hay como máximo una abierta por hospitalización y tipo (índice único parcial): si ya existe,
/// se actualizan sus valores y solo se vuelve a notificar si <b>subió</b> la prioridad o el nivel. Ninguna alerta de
/// riesgo, caída o preocupación clínica se cierra sola.
/// </summary>
public sealed class ServicioAlertas(
    IRepositorioAlertas alertas,
    ContextoHospitalizaciones contextos,
    DestinatariosAlertas destinatarios,
    NotificadorAlertas notificador,
    IUnidadDeTrabajo unidadDeTrabajo,
    IBitacoraFallos bitacora,
    IReloj reloj)
{
    public async Task<AlertaClinica?> DispararAsync(DisparoAlerta disparo, CancellationToken ct = default)
    {
        var contexto = await contextos.ObtenerAsync(disparo.HospitalizacionId, ct);
        if (contexto is not { Activa: true })
        {
            return null;
        }

        for (var intento = 1; intento <= 2; intento++)
        {
            var ahora = reloj.AhoraUtc;
            var abierta = await alertas.ObtenerAbiertaAsync(disparo.HospitalizacionId, disparo.Tipo, ct);
            AlertaClinica alerta;
            string? motivo;
            if (abierta is null)
            {
                alerta = AlertaClinica.Crear(
                    contexto.HospitalizacionId, contexto.PacienteId, contexto.ServicioId, disparo.Tipo, disparo.Prioridad,
                    disparo.NivelRiesgo, disparo.News2, disparo.Mews, disparo.Detalle, ahora);
                alertas.Agregar(alerta);
                motivo = MotivosNotificacion.Nueva;
            }
            else
            {
                if (!abierta.Cambiaria(disparo.Prioridad, disparo.NivelRiesgo, disparo.News2, disparo.Mews))
                {
                    return abierta;
                }

                alerta = abierta;
                motivo = abierta.Actualizar(disparo.Prioridad, disparo.NivelRiesgo, disparo.News2, disparo.Mews, disparo.Detalle, ahora)
                    ? MotivosNotificacion.Subio
                    : null;
            }

            var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
            if (!guardado.EsExito)
            {
                if (guardado.Error.Codigo == ErroresAlerta.AbiertaDuplicada.Codigo && intento == 1)
                {
                    continue; // otro disparador la creó a la vez: se actualiza esa
                }

                bitacora.Registrar($"Alerta {disparo.Tipo} de la hospitalización {disparo.HospitalizacionId}", new InvalidOperationException(guardado.Error.Codigo));
                return null;
            }

            if (motivo is null)
            {
                await notificador.SincronizarAsync(alerta, contexto.CodigoCama, ct);
            }
            else
            {
                await notificador.NotificarAsync(alerta, contexto.CodigoCama, motivo, await destinatarios.ParaAsync(alerta, motivo, ct), ct);
            }

            return alerta;
        }

        return null;
    }

    /// <summary>Volvió la señal o llegó la toma: cierra la alerta abierta de ese tipo con "Resuelta automáticamente".</summary>
    public async Task ResolverAsync(Guid hospitalizacionId, TipoAlerta tipo, CancellationToken ct = default)
    {
        var abierta = await alertas.ObtenerAbiertaAsync(hospitalizacionId, tipo, ct);
        if (abierta is null)
        {
            return;
        }

        abierta.ResolverAutomaticamente(reloj.AhoraUtc);
        if ((await unidadDeTrabajo.GuardarCambiosAsync(ct)).EsExito && await contextos.ObtenerAsync(hospitalizacionId, ct) is { } contexto)
        {
            await notificador.SincronizarAsync(abierta, contexto.CodigoCama, ct);
        }
    }

    /// <summary>Escala una alerta pendiente (alta: médicos y todas las enfermeras del servicio; media: médicos). Una sola vez.</summary>
    public async Task EscalarAsync(AlertaClinica alerta, CancellationToken ct = default)
    {
        alerta.MarcarEscalada(reloj.AhoraUtc);
        if (!(await unidadDeTrabajo.GuardarCambiosAsync(ct)).EsExito || await contextos.ObtenerAsync(alerta.HospitalizacionId, ct) is not { } contexto)
        {
            return;
        }

        await notificador.NotificarAsync(
            alerta, contexto.CodigoCama, MotivosNotificacion.Escalamiento, await destinatarios.ParaAsync(alerta, MotivosNotificacion.Escalamiento, ct), ct);
    }
}
