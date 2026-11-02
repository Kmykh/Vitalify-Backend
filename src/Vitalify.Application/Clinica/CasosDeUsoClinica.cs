using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Clinica;

/// <summary>Arma la evaluación para la respuesta: recomendación, próxima toma manual y avisos.</summary>
public sealed class PresentadorRiesgo(IRepositorioObservaciones observaciones, OpcionesClinicas opciones)
{
    public async Task<EvaluacionRiesgoDto> PresentarAsync(EvaluacionRiesgo evaluacion, CancellationToken ct = default)
    {
        var recientes = (await observaciones.ListarDesdeAsync(
                evaluacion.HospitalizacionId, evaluacion.EvaluadaEn - opciones.VigenciaObservaciones, ct))
            .Where(o => o.RegistradaEn <= evaluacion.EvaluadaEn)
            .ToList();

        var ultimaToma = recientes.FirstOrDefault()?.ObservadaEn
            ?? (await observaciones.ObtenerUltimaTomaAsync(evaluacion.HospitalizacionId, ct))?.ObservadaEn;

        return EvaluacionRiesgoDto.Desde(evaluacion, ultimaToma, AvisosClinicos.Generar(evaluacion, recientes));
    }
}

/// <summary>
/// Guarda una toma manual y recalcula el riesgo en el historial. Lo comparten <see cref="RegistrarObservacion"/> y la
/// confirmación de la vinculación por QR (observación inicial), para no duplicar las reglas.
/// </summary>
public sealed class ServicioObservaciones(
    IRepositorioObservaciones observaciones,
    EvaluadorRiesgo evaluador,
    IUnidadDeTrabajoHistorial unidadHistorial,
    PresentadorRiesgo presentador,
    RangosFisiologicos rangos)
{
    /// <summary>Construye la observación en el dominio (lanza <c>ExcepcionDeDominio</c> si es inválida). No guarda.</summary>
    public ObservacionEnfermeria Construir(Hospitalizacion hospitalizacion, TomaManualComando toma, Guid usuarioId, DateTime ahora)
    {
        // Un reloj de la tablet algo adelantado no debe dejar la medición "después" de su registro.
        var observadaEn = toma.ObservadaEn?.ToUniversalTime() is { } en && en < ahora ? en : ahora;
        return ObservacionEnfermeria.Registrar(
            hospitalizacion.Id, hospitalizacion.PacienteId, observadaEn, ahora, usuarioId, TomaManualValidador.Datos(toma, rangos), rangos);
    }

    public async Task<Resultado<EvaluacionRiesgoDto>> GuardarYEvaluarAsync(
        Hospitalizacion hospitalizacion, ObservacionEnfermeria observacion, CancellationToken ct = default)
    {
        observaciones.Agregar(observacion);
        var evaluacion = await evaluador.EvaluarAsync(
            hospitalizacion.Id, hospitalizacion.PacienteId, OrigenEvaluacion.Observacion, hospitalizacion.EscalaSpo2, ct);

        var guardado = await unidadHistorial.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        return await presentador.PresentarAsync(evaluacion, ct);
    }
}

public sealed record RegistrarObservacionComando(Guid PacienteId, TomaManualComando Toma, Guid UsuarioId, string? Ip);

public sealed record ObservacionRegistradaDto(Guid Id, DateTime ObservadaEn, EvaluacionRiesgoDto Riesgo);

/// <summary>
/// Toma manual del personal clínico (asistente de la fase 5 o formato plano de la fase 4). Se recalcula el riesgo en
/// el acto y la respuesta trae la recomendación, la próxima toma y los avisos.
/// </summary>
public sealed class RegistrarObservacion(
    IValidator<TomaManualComando> validador,
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    ServicioObservaciones servicio,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadTransaccional,
    IReloj reloj)
{
    public async Task<Resultado<ObservacionRegistradaDto>> EjecutarAsync(RegistrarObservacionComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando.Toma, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        if (await pacientes.ObtenerPorIdAsync(comando.PacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var hospitalizacion = await hospitalizaciones.ObtenerActivaDePacienteAsync(comando.PacienteId, ct);
        if (hospitalizacion is null)
        {
            return ErroresPaciente.SinHospitalizacionActivaNoEncontrada;
        }

        var ahora = reloj.AhoraUtc;
        var observacion = servicio.Construir(hospitalizacion, comando.Toma, comando.UsuarioId, ahora);
        var riesgo = await servicio.GuardarYEvaluarAsync(hospitalizacion, observacion, ct);
        if (!riesgo.EsExito)
        {
            return riesgo.Error;
        }

        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.ObservacionRegistrada, ahora, comando.UsuarioId, comando.Ip,
            $"Observación {observacion.Id} de la hospitalización {hospitalizacion.Id}."));
        await unidadTransaccional.GuardarCambiosAsync(ct);

        return new ObservacionRegistradaDto(observacion.Id, observacion.ObservadaEn, riesgo.Valor);
    }
}

/// <summary>Última evaluación NEWS2/MEWS de la hospitalización activa, con el desglose por parámetro.</summary>
public sealed class ObtenerRiesgoPaciente(
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioObservaciones observaciones,
    PresentadorRiesgo presentador)
{
    public async Task<Resultado<RiesgoPacienteDto>> EjecutarAsync(Guid pacienteId, CancellationToken ct = default)
    {
        if (await pacientes.ObtenerPorIdAsync(pacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var hospitalizacion = await hospitalizaciones.ObtenerActivaDePacienteAsync(pacienteId, ct);
        if (hospitalizacion is null)
        {
            return ErroresPaciente.SinHospitalizacionActivaNoEncontrada;
        }

        return await DtoAsync(hospitalizacion, ct);
    }

    internal async Task<RiesgoPacienteDto> DtoAsync(Hospitalizacion hospitalizacion, CancellationToken ct)
    {
        var ultima = await evaluaciones.ObtenerUltimaAsync(hospitalizacion.Id, ct);
        var brazo = await observaciones.ObtenerUltimoBrazoPresionAsync(hospitalizacion.Id, ct);
        return new RiesgoPacienteDto(
            hospitalizacion.PacienteId, hospitalizacion.Id,
            ultima is null ? null : await presentador.PresentarAsync(ultima, ct),
            hospitalizacion.EscalaSpo2.ToString(), brazo?.ToString());
    }
}

/// <param name="Escala"><c>Escala1</c> o <c>Escala2</c>.</param>
/// <param name="Motivo">Por qué se indica (de 3 a 300 caracteres). No va a la auditoría.</param>
public sealed record CambiarEscalaSpo2Comando(Guid PacienteId, string? Escala, string? Motivo, Guid UsuarioId, string? Ip);

public sealed class CambiarEscalaSpo2Validador : AbstractValidator<CambiarEscalaSpo2Comando>
{
    public CambiarEscalaSpo2Validador()
    {
        RuleFor(c => c.Escala)
            .Must(e => Enumeraciones.TryParseNombre<EscalaSpo2>(e, out _))
            .WithMessage($"La escala debe ser una de: {Enumeraciones.Nombres<EscalaSpo2>()}.");
        RuleFor(c => c.Motivo)
            .Must(m => m?.Trim().Length is >= Hospitalizacion.LargoMinimoMotivoEscala and <= Hospitalizacion.LargoMaximoMotivoEscala)
            .WithMessage($"El motivo debe tener entre {Hospitalizacion.LargoMinimoMotivoEscala} y {Hospitalizacion.LargoMaximoMotivoEscala} caracteres.");
    }
}

/// <summary>
/// El médico indica la escala de SpO2 del paciente (la 2 para la insuficiencia respiratoria hipercápnica). Se guarda en
/// la hospitalización activa y el riesgo se recalcula al instante con la escala nueva; las lecturas siguientes del
/// wearable también la usan (se invalida la caché del dispositivo).
/// </summary>
public sealed class CambiarEscalaSpo2(
    IValidator<CambiarEscalaSpo2Comando> validador,
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioDispositivos dispositivos,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    IResolutorDispositivos resolutorDispositivos,
    EvaluadorRiesgo evaluador,
    IUnidadDeTrabajoHistorial unidadHistorial,
    ObtenerRiesgoPaciente obtenerRiesgo,
    IReloj reloj)
{
    public async Task<Resultado<RiesgoPacienteDto>> EjecutarAsync(CambiarEscalaSpo2Comando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        if (await pacientes.ObtenerPorIdAsync(comando.PacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var hospitalizacion = await hospitalizaciones.ObtenerActivaDePacienteAsync(comando.PacienteId, ct);
        if (hospitalizacion is null)
        {
            return ErroresPaciente.SinHospitalizacionActivaNoEncontrada;
        }

        var ahora = reloj.AhoraUtc;
        Enumeraciones.TryParseNombre<EscalaSpo2>(comando.Escala, out var escala);
        hospitalizacion.CambiarEscalaSpo2(escala, comando.Motivo!, ahora, comando.UsuarioId);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.EscalaSpo2Cambiada, ahora, comando.UsuarioId, comando.Ip,
            $"Hospitalización {hospitalizacion.Id}: escala de SpO2 {escala}."));

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        if (await hospitalizaciones.ObtenerAsignacionVigenteAsync(hospitalizacion.Id, ct) is { } asignacion
            && await dispositivos.ObtenerPorIdAsync(asignacion.DispositivoId, ct) is { } dispositivo)
        {
            resolutorDispositivos.Invalidar(dispositivo.Codigo);
        }

        await evaluador.EvaluarAsync(hospitalizacion.Id, hospitalizacion.PacienteId, OrigenEvaluacion.CambioEscala, escala, ct);
        var evaluado = await unidadHistorial.GuardarCambiosAsync(ct);
        if (!evaluado.EsExito)
        {
            return evaluado.Error;
        }

        return await obtenerRiesgo.DtoAsync(hospitalizacion, ct);
    }
}
