using System.Globalization;
using Vitalify.Application.Alertas;
using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Historial;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;
using Vitalify.Application.Turnos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Nnn;
using Vitalify.Domain.Turnos;

namespace Vitalify.Application.Nnn;

/// <summary>Lo que el motor sugiere ahora para una hospitalización, con la evaluación en la que se basa.</summary>
public sealed record SugerenciasCalculadas(EvaluacionRiesgo? Evaluacion, IReadOnlyList<SugerenciaNnn> Sugerencias, bool Cambio = false);

/// <summary>
/// Calcula las sugerencias NNN de una hospitalización con el motor del dominio y lleva el registro de cada una
/// (<c>plan_sugerencia</c>): emite las nuevas y expira las que el motor dejó de proponer. Un descarte vale hasta el fin de
/// la guardia en que se registró, para que la siguiente enfermera vuelva a valorarlo.
/// </summary>
public sealed class ServicioSugerenciasNnn(
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioObservaciones observaciones,
    IRepositorioAlertas alertas,
    IRepositorioPlanCuidados plan,
    IUnidadDeTrabajo unidadDeTrabajo,
    IBitacoraFallos bitacora,
    CatalogoTurnos catalogo,
    OpcionesClinicas opcionesClinicas,
    IReloj reloj)
{
    public async Task<SugerenciasCalculadas> CalcularAsync(Guid hospitalizacionId, CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var evaluacion = await evaluaciones.ObtenerUltimaAsync(hospitalizacionId, ct);
        var abiertas = (await alertas.ListarDeHospitalizacionAsync(hospitalizacionId, ct)).Select(a => a.Alerta).Where(a => a.EstaAbierta).ToList();
        var toma = await observaciones.ObtenerUltimaTomaAsync(hospitalizacionId, ct);
        if (toma is not null && !FrecuenciaTomaManual.Cuenta(toma, ahora, evaluacion, opcionesClinicas.ToleranciaToma))
        {
            toma = null; // una toma vencida no cuenta: su dolor ya no es actual
        }

        var activos = await plan.ListarCodigosActivosAsync(hospitalizacionId, ct);
        var descartados = await plan.ListarCodigosDescartadosDesdeAsync(hospitalizacionId, Guardias.Actual(catalogo, ahora).Intervalo.InicioUtc, ct);
        var sugerencias = MotorSugerenciasNnn.Estandar.Sugerir(new EntradaMotorNnn(evaluacion, abiertas, toma, activos, descartados));
        return new SugerenciasCalculadas(evaluacion, sugerencias);
    }

    /// <summary>Calcula y actualiza el registro de sugerencias. <see cref="SugerenciasCalculadas.Cambio"/> dice si hubo nuevas o expiradas.</summary>
    public async Task<SugerenciasCalculadas> SincronizarAsync(Guid hospitalizacionId, Guid pacienteId, CancellationToken ct = default)
    {
        var calculadas = await CalcularAsync(hospitalizacionId, ct);
        var ahora = reloj.AhoraUtc;
        var pendientes = await plan.ListarSugerenciasPendientesAsync(hospitalizacionId, ct);
        var cambio = false;

        foreach (var s in calculadas.Sugerencias.Where(s => pendientes.All(p => p.DiagnosticoCodigo != s.DiagnosticoCodigo)))
        {
            plan.Agregar(SugerenciaEmitida.Emitir(hospitalizacionId, pacienteId, s, MotorSugerenciasNnn.VersionReglas, ahora));
            cambio = true;
        }

        foreach (var p in pendientes.Where(p => calculadas.Sugerencias.All(s => s.DiagnosticoCodigo != p.DiagnosticoCodigo)))
        {
            p.Expirar(ahora);
            cambio = true;
        }

        if (cambio)
        {
            var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
            if (!guardado.EsExito && guardado.Error.Codigo != ErroresNnn.SugerenciaConcurrente.Codigo)
            {
                bitacora.Registrar($"Registro de sugerencias NNN de la hospitalización {hospitalizacionId}", new InvalidOperationException(guardado.Error.Codigo));
            }
        }

        return calculadas with { Cambio = cambio };
    }
}

/// <summary>
/// Avisa por SignalR que cambió el plan (<c>planActualizado</c>) o cuántas sugerencias hay (<c>sugerenciasNnn</c>). Solo ids
/// y una cantidad: nunca un código ni una etiqueta NANDA-I. Un fallo queda en la bitácora y no corta el flujo.
/// </summary>
public sealed class NotificadorPlanCuidados(ContextoHospitalizaciones contextos, INotificadorTiempoReal tiempoReal, IBitacoraFallos bitacora)
{
    public async Task PlanActualizadoAsync(Guid hospitalizacionId, CancellationToken ct = default)
    {
        try
        {
            if (await contextos.ObtenerAsync(hospitalizacionId, ct) is { } c)
            {
                await tiempoReal.PlanActualizadoAsync(c.ServicioId, new PlanActualizadoDto(c.PacienteId, c.HospitalizacionId), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            bitacora.Registrar($"Tiempo real del plan de la hospitalización {hospitalizacionId}", ex);
        }
    }

    public async Task SugerenciasAsync(Guid hospitalizacionId, int cantidad, CancellationToken ct = default)
    {
        try
        {
            if (await contextos.ObtenerAsync(hospitalizacionId, ct) is { } c)
            {
                await tiempoReal.SugerenciasNnnAsync(c.ServicioId, new SugerenciasNnnDto(c.PacienteId, cantidad), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            bitacora.Registrar($"Tiempo real de las sugerencias de la hospitalización {hospitalizacionId}", ex);
        }
    }
}

/// <summary>
/// Con cada evaluación <b>guardada</b> (la que cambió algo) se recalculan las sugerencias; si cambiaron, se avisa al servicio.
/// Se registra después de <see cref="AlertaDeRiesgoAlEvaluar"/>, para asociar las sugerencias a la alerta de riesgo.
/// </summary>
public sealed class SugerenciasNnnAlEvaluar(ServicioSugerenciasNnn servicio, NotificadorPlanCuidados notificador) : IManejadorRiesgoEvaluado
{
    public async Task ManejarAsync(RiesgoEvaluado evento, CancellationToken ct = default)
    {
        if (!evento.Guardada)
        {
            return;
        }

        var calculadas = await servicio.SincronizarAsync(evento.HospitalizacionId, evento.PacienteId, ct);
        if (calculadas.Cambio)
        {
            await notificador.SugerenciasAsync(evento.HospitalizacionId, calculadas.Sugerencias.Count, ct);
        }
    }
}

/// <summary>Una caída abre una alerta y sugiere "Riesgo de caídas del adulto". Se registra después de <see cref="AlertasAlRegistrarLectura"/>.</summary>
public sealed class SugerenciasNnnAlRegistrarLectura(ServicioSugerenciasNnn servicio, NotificadorPlanCuidados notificador) : IManejadorLecturaRegistrada
{
    public async Task ManejarAsync(LecturaRegistrada lectura, CancellationToken ct = default)
    {
        if (!lectura.Caida)
        {
            return;
        }

        var calculadas = await servicio.SincronizarAsync(lectura.HospitalizacionId, lectura.PacienteId, ct);
        if (calculadas.Cambio)
        {
            await notificador.SugerenciasAsync(lectura.HospitalizacionId, calculadas.Sugerencias.Count, ct);
        }
    }
}

public sealed record ObtenerSugerenciasConsulta(Guid PacienteId, Guid UsuarioId, string? Ip);

/// <summary>Sugerencias actuales de la hospitalización activa, con su motivo y la versión de las reglas. Queda auditado.</summary>
public sealed class ObtenerSugerenciasPaciente(
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    ServicioSugerenciasNnn servicio,
    PresentadorPlanCuidados presentador,
    NotificadorPlanCuidados notificador,
    AuditoriaHistorial auditoria)
{
    public async Task<Resultado<SugerenciasPacienteDto>> EjecutarAsync(ObtenerSugerenciasConsulta consulta, CancellationToken ct = default)
    {
        if (await pacientes.ObtenerPorIdAsync(consulta.PacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var hospitalizacion = await hospitalizaciones.ObtenerActivaDePacienteAsync(consulta.PacienteId, ct);
        if (hospitalizacion is null)
        {
            return ErroresPaciente.SinHospitalizacionActiva;
        }

        var calculadas = await servicio.SincronizarAsync(hospitalizacion.Id, consulta.PacienteId, ct);
        var auditado = await auditoria.RegistrarAsync(consulta.PacienteId, hospitalizacion.Id, "sugerencias-nnn", consulta.UsuarioId, consulta.Ip, ct);
        if (!auditado.EsExito)
        {
            return auditado.Error;
        }

        if (calculadas.Cambio)
        {
            await notificador.SugerenciasAsync(hospitalizacion.Id, calculadas.Sugerencias.Count, ct);
        }

        var catalogo = await presentador.CatalogoAsync(ct);
        var e = calculadas.Evaluacion;
        return new SugerenciasPacienteDto(
            consulta.PacienteId, hospitalizacion.Id, MotorSugerenciasNnn.VersionReglas, e?.Id, e?.EvaluadaEn, e?.News2Total,
            e is null ? null : e.News2Completo && e.MewsCompleto,
            calculadas.Sugerencias.Select(s => PresentadorPlanCuidados.Sugerencia(catalogo, s)).ToList(), TextosNnn.Aviso);
    }
}

public sealed record ObtenerSugerenciasAlertaConsulta(Guid AlertaId, Guid UsuarioId, string? Ip);

/// <summary>
/// Las sugerencias de la evaluación que abrió una alerta: la de su <c>detalle.evaluacionId</c> (riesgo) o la última evaluación
/// hasta que se creó (caída, sin señal...), con la toma manual que contaba entonces y esa misma alerta. Los diagnósticos que ya
/// están activos en el plan no se repiten. No se registra como emisión: es una consulta del pasado.
/// </summary>
public sealed class ObtenerSugerenciasAlerta(
    IRepositorioAlertas alertas,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioObservaciones observaciones,
    IRepositorioPlanCuidados plan,
    PresentadorPlanCuidados presentador,
    AuditoriaHistorial auditoria,
    OpcionesClinicas opcionesClinicas)
{
    public async Task<Resultado<SugerenciasAlertaDto>> EjecutarAsync(ObtenerSugerenciasAlertaConsulta consulta, CancellationToken ct = default)
    {
        var alerta = await alertas.ObtenerPorIdAsync(consulta.AlertaId, ct);
        if (alerta is null)
        {
            return ErroresAlerta.NoEncontrada;
        }

        var evaluacion = alerta.Detalle?.GetValueOrDefault("evaluacionId") is { } texto && Guid.TryParse(texto, CultureInfo.InvariantCulture, out var id)
            ? await evaluaciones.ObtenerPorIdAsync(id, ct)
            : null;
        evaluacion ??= (await evaluaciones.ListarAsync(alerta.HospitalizacionId, alerta.CreadaEn - FrecuenciaTomaManual.TopeVigencia, alerta.CreadaEn, ct))
            .LastOrDefault();

        var instante = evaluacion?.EvaluadaEn ?? alerta.CreadaEn;
        var toma = (await observaciones.ListarAsync(alerta.HospitalizacionId, instante - FrecuenciaTomaManual.TopeVigencia, instante, ct))
            .FirstOrDefault(o => FrecuenciaTomaManual.Cuenta(o, instante, evaluacion, opcionesClinicas.ToleranciaToma));

        var activos = await plan.ListarCodigosActivosAsync(alerta.HospitalizacionId, ct);
        var sugerencias = MotorSugerenciasNnn.Estandar.Sugerir(new EntradaMotorNnn(evaluacion, [alerta], toma, activos));

        var auditado = await auditoria.RegistrarAsync(
            alerta.PacienteId, alerta.HospitalizacionId, $"sugerencias-nnn de la alerta {alerta.Id}", consulta.UsuarioId, consulta.Ip, ct);
        if (!auditado.EsExito)
        {
            return auditado.Error;
        }

        var catalogo = await presentador.CatalogoAsync(ct);
        return new SugerenciasAlertaDto(
            alerta.Id, alerta.PacienteId, alerta.HospitalizacionId, MotorSugerenciasNnn.VersionReglas, evaluacion?.Id, evaluacion?.EvaluadaEn,
            sugerencias.Select(s => PresentadorPlanCuidados.Sugerencia(catalogo, s)).ToList(), TextosNnn.Aviso);
    }
}
