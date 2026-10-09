using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Clinica;

/// <param name="Conciencia"><c>Alerta</c>, <c>ConfusionNueva</c>, <c>RespondeVoz</c>, <c>RespondeDolor</c> o <c>NoResponde</c>.</param>
/// <param name="ObservadaEn">Momento de la medición; si falta, ahora.</param>
public sealed record RegistrarObservacionComando(
    Guid PacienteId, int? Fr, int? Pas, int? Pad, decimal? Temperatura, string? Conciencia, bool? OxigenoSuplementario,
    DateTime? ObservadaEn, Guid UsuarioId, string? Ip);

public sealed class RegistrarObservacionValidador : AbstractValidator<RegistrarObservacionComando>
{
    public RegistrarObservacionValidador(RangosFisiologicos rangos, OpcionesClinicas opciones, IReloj reloj)
    {
        RuleFor(c => c)
            .Must(c => c.Fr is not null || c.Pas is not null || c.Pad is not null || c.Temperatura is not null
                       || !string.IsNullOrWhiteSpace(c.Conciencia) || c.OxigenoSuplementario is not null)
            .WithName("observacion").OverridePropertyName("observacion")
            .WithMessage("Registra al menos un valor: fr, pas y pad, temperatura, conciencia u oxigenoSuplementario.");

        RuleFor(c => c.Fr).Must(v => v is null || rangos.EsValido(VariableSigno.Fr, v.Value))
            .WithMessage($"fr debe estar entre {rangos.Fr}.");
        RuleFor(c => c.Temperatura).Must(v => v is null || rangos.EsValido(VariableSigno.Temperatura, v.Value))
            .WithMessage($"temperatura debe estar entre {rangos.Temperatura} °C.");
        RuleFor(c => c.Pas)
            .Must((c, pas) => (pas is null) == (c.Pad is null)).WithMessage("La presión necesita pas y pad.")
            .Must((c, pas) => pas is null || c.Pad is null || pas > c.Pad).WithMessage("pas debe ser mayor que pad.")
            .Must(v => v is null || rangos.EsValido(VariableSigno.Pas, v.Value)).WithMessage($"pas debe estar entre {rangos.Pas}.");
        RuleFor(c => c.Pad).Must(v => v is null || rangos.EsValido(VariableSigno.Pad, v.Value))
            .WithMessage($"pad debe estar entre {rangos.Pad}.");
        RuleFor(c => c.Conciencia)
            .Must(v => string.IsNullOrWhiteSpace(v) || Enumeraciones.TryParseNombre<NivelConciencia>(v, out _))
            .WithMessage($"conciencia debe ser una de: {Enumeraciones.Nombres<NivelConciencia>()}.");
        RuleFor(c => c.ObservadaEn)
            .Must(v => v is null || v.Value.ToUniversalTime() <= reloj.AhoraUtc.AddMinutes(1))
            .WithMessage("observadaEn no puede estar en el futuro.")
            .Must(v => v is null || v.Value.ToUniversalTime() >= reloj.AhoraUtc - opciones.VigenciaObservaciones)
            .WithMessage($"observadaEn no puede tener más de {opciones.VigenciaObservaciones.TotalHours:0} horas: ya no contaría para el puntaje.");
    }
}

public sealed record ObservacionRegistradaDto(Guid Id, DateTime ObservadaEn, EvaluacionRiesgoDto Riesgo);

/// <summary>
/// El personal clínico registra lo que el wearable no mide (FR, presión, conciencia, oxígeno suplementario) o la
/// temperatura de un termómetro clínico. Se recalcula el riesgo en el acto.
/// </summary>
public sealed class RegistrarObservacion(
    IValidator<RegistrarObservacionComando> validador,
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioObservaciones observaciones,
    EvaluadorRiesgo evaluador,
    IUnidadDeTrabajoHistorial unidadHistorial,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadTransaccional,
    RangosFisiologicos rangos,
    IReloj reloj)
{
    public async Task<Resultado<ObservacionRegistradaDto>> EjecutarAsync(RegistrarObservacionComando comando, CancellationToken ct = default)
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
        NivelConciencia? conciencia = Enumeraciones.TryParseNombre<NivelConciencia>(comando.Conciencia, out var c) ? c : null;
        var observacion = ObservacionEnfermeria.Registrar(
            hospitalizacion.Id, hospitalizacion.PacienteId, comando.ObservadaEn?.ToUniversalTime() ?? ahora, ahora, comando.UsuarioId,
            comando.Fr, comando.Pas, comando.Pad, comando.Temperatura, conciencia, comando.OxigenoSuplementario, rangos);
        observaciones.Agregar(observacion);

        var evaluacion = await evaluador.EvaluarAsync(hospitalizacion.Id, hospitalizacion.PacienteId, OrigenEvaluacion.Observacion, ct);
        var guardado = await unidadHistorial.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.ObservacionRegistrada, ahora, comando.UsuarioId, comando.Ip,
            $"Observación {observacion.Id} de la hospitalización {hospitalizacion.Id}."));
        await unidadTransaccional.GuardarCambiosAsync(ct);

        return new ObservacionRegistradaDto(observacion.Id, observacion.ObservadaEn, EvaluacionRiesgoDto.Desde(evaluacion));
    }
}

/// <summary>Última evaluación NEWS2/MEWS de la hospitalización activa, con el desglose por parámetro.</summary>
public sealed class ObtenerRiesgoPaciente(
    IRepositorioPacientes pacientes, IRepositorioHospitalizaciones hospitalizaciones, IRepositorioEvaluaciones evaluaciones)
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

        var ultima = await evaluaciones.ObtenerUltimaAsync(hospitalizacion.Id, ct);
        return new RiesgoPacienteDto(pacienteId, hospitalizacion.Id, ultima is null ? null : EvaluacionRiesgoDto.Desde(ultima));
    }
}

/// <summary>
/// Registra el estado de conexión que publica cada wearable en <c>device/{codigo}/estado</c>. Lo llama el adaptador
/// MQTT.
/// </summary>
public sealed class RegistrarPresenciaDispositivo(IPresenciaDispositivos presencia, IReloj reloj)
{
    public const string EnLinea = "en-linea";
    public const string FueraDeLinea = "fuera-de-linea";
    public const string Desconocida = "desconocida";

    /// <returns>false si el texto no es <c>online</c> ni <c>offline</c>.</returns>
    public bool Ejecutar(string codigoDispositivo, string estado)
    {
        var texto = estado.Trim().ToLowerInvariant();
        if (texto is not ("online" or "offline"))
        {
            return false;
        }

        presencia.Registrar(codigoDispositivo.Trim().ToUpperInvariant(), new PresenciaDispositivo(texto == "online", reloj.AhoraUtc));
        return true;
    }

    public static string Texto(PresenciaDispositivo? presencia) =>
        presencia is null ? Desconocida : presencia.EnLinea ? EnLinea : FueraDeLinea;
}
