using FluentValidation;
using Vitalify.Application.Alertas;
using Vitalify.Application.Comun;
using Vitalify.Application.Historial;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Application.Turnos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Nnn;
using Vitalify.Domain.Turnos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Nnn;

internal static class ReglasPlan
{
    public const int MaximoElementos = 10;
    public const int MaximoIndicadores = 20;

    public static void Motivo<T>(IRuleBuilder<T, string?> regla) =>
        regla.Must(m => m?.Trim().Length is >= DiagnosticoPlan.LargoMinimoMotivo and <= DiagnosticoPlan.LargoMaximoMotivo)
            .WithMessage($"El motivo es obligatorio y debe tener entre {DiagnosticoPlan.LargoMinimoMotivo} y {DiagnosticoPlan.LargoMaximoMotivo} caracteres.");

    public static void Frecuencia<T>(IRuleBuilder<T, string?> regla) =>
        regla.Must(f => Enumeraciones.TryParseNombre<FrecuenciaIntervencion>(f, out _))
            .WithMessage($"La frecuencia debe ser {Enumeraciones.Nombres<FrecuenciaIntervencion>()}.");

    public static bool SinRepetir<T>(IReadOnlyList<T>? lista, Func<T, string?> clave) =>
        lista is null || lista.Select(clave).Distinct(StringComparer.Ordinal).Count() == lista.Count;
}

/// <summary>Lo que comparten los casos de uso del plan: hospitalización activa, alerta del episodio y rol del usuario.</summary>
public sealed class ContextoPlanCuidados(
    IRepositorioPacientes pacientes,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioAlertas alertas,
    IRepositorioUsuarios usuarios)
{
    public async Task<Resultado<Hospitalizacion>> HospitalizacionActivaAsync(Guid pacienteId, CancellationToken ct = default)
    {
        if (await pacientes.ObtenerPorIdAsync(pacienteId, ct) is null)
        {
            return ErroresPaciente.NoEncontrado;
        }

        var h = await hospitalizaciones.ObtenerActivaDePacienteAsync(pacienteId, ct);
        return h is null ? ErroresPaciente.SinHospitalizacionActiva : h;
    }

    /// <summary>La alerta indicada debe ser de la misma hospitalización (si no, error de validación en <paramref name="campo"/>).</summary>
    public async Task<Error?> ValidarAlertaAsync(Guid? alertaId, Guid hospitalizacionId, string campo, CancellationToken ct = default) =>
        alertaId is { } id && (await alertas.ObtenerPorIdAsync(id, ct))?.HospitalizacionId != hospitalizacionId
            ? ErroresComunes.ValidacionDeCampo(campo, "La alerta no existe o es de otra hospitalización.")
            : null;

    /// <summary>El médico solo puede agregar o registrar la NIC 7710; la enfermera, cualquiera.</summary>
    public async Task<Error?> ValidarRolAsync(Guid usuarioId, string intervencionCodigo, CancellationToken ct = default)
    {
        var usuario = await usuarios.ObtenerPorIdAsync(usuarioId, ct);
        return usuario?.Rol == Rol.Enfermera || (usuario?.Rol == Rol.Medico && intervencionCodigo == ReglasNnn.NicColaboracionMedica)
            ? null
            : ErroresNnn.SoloEnfermeria;
    }
}

public sealed record IntervencionSolicitada(string? Codigo, string? Frecuencia);

public sealed record ResultadoSolicitado(string? Codigo, int? PuntuacionBasal, int? PuntuacionMeta);

/// <param name="Origen"><c>Sugerido</c> (con <c>ReglaOrigen</c>) o <c>Manual</c>.</param>
public sealed record ConfirmarDiagnosticoComando(
    Guid PacienteId, string? DiagnosticoCodigo, string? Origen, string? ReglaOrigen, Guid? AlertaId,
    IReadOnlyList<IntervencionSolicitada>? Intervenciones, IReadOnlyList<ResultadoSolicitado>? Resultados, Guid UsuarioId, string? Ip);

public sealed class ConfirmarDiagnosticoValidador : AbstractValidator<ConfirmarDiagnosticoComando>
{
    public ConfirmarDiagnosticoValidador()
    {
        RuleFor(c => c.DiagnosticoCodigo).Must(CodigosNnn.EsDiagnostico).WithMessage("Debe ser un código NANDA-I de 5 dígitos (por ejemplo 00032).");
        RuleFor(c => c.Origen).Must(o => Enumeraciones.TryParseNombre<OrigenDiagnostico>(o, out _)).WithMessage("El origen debe ser Sugerido o Manual.");
        RuleFor(c => c.ReglaOrigen).Must(DiagnosticoPlan.EsReglaValida)
            .When(c => Enumeraciones.TryParseNombre<OrigenDiagnostico>(c.Origen, out var o) && o == OrigenDiagnostico.Sugerido)
            .WithMessage("Una sugerencia confirmada debe indicar la regla que la propuso (por ejemplo R1).");

        RuleFor(c => c.Intervenciones)
            .Must(l => l is { Count: > 0 and <= ReglasPlan.MaximoElementos })
            .WithMessage($"Planifica de 1 a {ReglasPlan.MaximoElementos} intervenciones NIC.")
            .Must(l => ReglasPlan.SinRepetir(l, i => i?.Codigo)).WithMessage("No repitas intervenciones.");
        RuleForEach(c => c.Intervenciones).NotNull().ChildRules(i =>
        {
            i.RuleFor(x => x.Codigo).Must(CodigosNnn.EsIntervencion).WithMessage("Debe ser un código NIC de 4 dígitos.");
            ReglasPlan.Frecuencia(i.RuleFor(x => x.Frecuencia));
        });

        RuleFor(c => c.Resultados)
            .Must(l => l is { Count: > 0 and <= ReglasPlan.MaximoElementos })
            .WithMessage($"Planifica de 1 a {ReglasPlan.MaximoElementos} resultados NOC.")
            .Must(l => ReglasPlan.SinRepetir(l, r => r?.Codigo)).WithMessage("No repitas resultados.");
        RuleForEach(c => c.Resultados).NotNull().ChildRules(r =>
        {
            r.RuleFor(x => x.Codigo).Must(CodigosNnn.EsResultado).WithMessage("Debe ser un código NOC de 4 dígitos.");
            r.RuleFor(x => x.PuntuacionBasal).Must(p => p is { } v && ValoracionNocSugerida.EsValida(v)).WithMessage("La puntuación basal va de 1 a 5.");
            r.RuleFor(x => x.PuntuacionMeta).Must(p => p is { } v && ValoracionNocSugerida.EsValida(v)).WithMessage("La puntuación meta va de 1 a 5.")
                .Must((x, meta) => meta >= x.PuntuacionBasal).When(x => x.PuntuacionBasal is not null).WithMessage("La meta debe ser mayor o igual que la basal.");
        });
    }
}

/// <summary>
/// La enfermera confirma un diagnóstico (sugerido por el motor o elegido del catálogo) con sus intervenciones NIC y
/// resultados NOC: queda Activo en el plan de la hospitalización activa. Un mismo diagnóstico no puede estar activo dos veces
/// (409 <c>diagnostico-ya-activo</c>). La auditoría lleva solo ids, nunca el código ni la etiqueta.
/// </summary>
public sealed class ConfirmarDiagnostico(
    IValidator<ConfirmarDiagnosticoComando> validador,
    ContextoPlanCuidados contexto,
    IRepositorioCatalogoNnn catalogo,
    IRepositorioPlanCuidados plan,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    PresentadorPlanCuidados presentador,
    ServicioSugerenciasNnn sugerencias,
    NotificadorPlanCuidados notificador,
    IReloj reloj)
{
    public async Task<Resultado<DiagnosticoPlanDto>> EjecutarAsync(ConfirmarDiagnosticoComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var hospitalizacion = await contexto.HospitalizacionActivaAsync(comando.PacienteId, ct);
        if (!hospitalizacion.EsExito)
        {
            return hospitalizacion.Error;
        }

        var h = hospitalizacion.Valor;
        var c = await catalogo.ObtenerAsync(ct);
        if (ValidarCatalogo(c, comando) is { } errorCatalogo)
        {
            return errorCatalogo;
        }

        if (await contexto.ValidarAlertaAsync(comando.AlertaId, h.Id, "alertaId", ct) is { } errorAlerta)
        {
            return errorAlerta;
        }

        if (await plan.ExisteActivoAsync(h.Id, comando.DiagnosticoCodigo!, ct))
        {
            return ErroresNnn.DiagnosticoYaActivo;
        }

        Enumeraciones.TryParseNombre<OrigenDiagnostico>(comando.Origen, out var origen);
        var ahora = reloj.AhoraUtc;
        var pendiente = (await plan.ListarSugerenciasPendientesAsync(h.Id, ct)).FirstOrDefault(s => s.DiagnosticoCodigo == comando.DiagnosticoCodigo);
        var evaluacionId = pendiente?.EvaluacionId ?? (await evaluaciones.ObtenerUltimaAsync(h.Id, ct))?.Id;

        DiagnosticoPlan d;
        List<IntervencionPlan> intervenciones;
        List<ResultadoPlan> resultados;
        try
        {
            d = DiagnosticoPlan.Confirmar(
                h.Id, h.PacienteId, comando.DiagnosticoCodigo!, origen, comando.ReglaOrigen?.Trim(), comando.AlertaId ?? pendiente?.AlertaId,
                evaluacionId, MotorSugerenciasNnn.VersionReglas, comando.UsuarioId, ahora);
            intervenciones = comando.Intervenciones!.Select(i =>
            {
                Enumeraciones.TryParseNombre<FrecuenciaIntervencion>(i.Frecuencia, out var frecuencia);
                return IntervencionPlan.Planificar(d.Id, i.Codigo!, frecuencia, comando.UsuarioId, ahora);
            }).ToList();
            resultados = comando.Resultados!.Select(r => ResultadoPlan.Planificar(d.Id, r.Codigo!, r.PuntuacionBasal!.Value, r.PuntuacionMeta!.Value)).ToList();
        }
        catch (ExcepcionDeDominio ex)
        {
            return ErroresComunes.ValidacionDeCampo("diagnosticoCodigo", ex.Message);
        }

        plan.Agregar(d);
        intervenciones.ForEach(plan.Agregar);
        resultados.ForEach(plan.Agregar);
        pendiente?.Confirmar(d.Id, ahora);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.ConfirmarDiagnosticoEnfermeria, ahora, comando.UsuarioId, comando.Ip,
            $"Diagnóstico de enfermería {d.Id} de la hospitalización {h.Id}."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        await notificador.PlanActualizadoAsync(h.Id, ct);
        var quedan = await sugerencias.SincronizarAsync(h.Id, h.PacienteId, ct);
        await notificador.SugerenciasAsync(h.Id, quedan.Sugerencias.Count, ct);
        return (await presentador.PresentarAsync(new PlanCuidadosLectura(h.Id, [d], intervenciones, [], resultados, []), ct))[0];
    }

    private static Error? ValidarCatalogo(CatalogoNnnLectura c, ConfirmarDiagnosticoComando comando)
    {
        if (c.Diagnostico(comando.DiagnosticoCodigo) is not { Activo: true })
        {
            return ErroresComunes.ValidacionDeCampo("diagnosticoCodigo", "El diagnóstico no está en el catálogo NANDA-I.");
        }

        for (var i = 0; i < comando.Intervenciones!.Count; i++)
        {
            if (c.Intervencion(comando.Intervenciones[i].Codigo) is not { Activo: true })
            {
                return ErroresComunes.ValidacionDeCampo($"intervenciones[{i}].codigo", "La intervención no está en el catálogo NIC.");
            }
        }

        for (var i = 0; i < comando.Resultados!.Count; i++)
        {
            if (c.Resultado(comando.Resultados[i].Codigo) is not { Activo: true })
            {
                return ErroresComunes.ValidacionDeCampo($"resultados[{i}].codigo", "El resultado no está en el catálogo NOC.");
            }
        }

        return null;
    }
}

public sealed record DescartarSugerenciaComando(Guid PacienteId, string? DiagnosticoCodigo, string? ReglaOrigen, string? Motivo, Guid UsuarioId, string? Ip);

public sealed class DescartarSugerenciaValidador : AbstractValidator<DescartarSugerenciaComando>
{
    public DescartarSugerenciaValidador()
    {
        RuleFor(c => c.DiagnosticoCodigo).Must(CodigosNnn.EsDiagnostico).WithMessage("Debe ser un código NANDA-I de 5 dígitos (por ejemplo 00032).");
        RuleFor(c => c.ReglaOrigen).Must(DiagnosticoPlan.EsReglaValida).WithMessage("Indica la regla que propuso la sugerencia (por ejemplo R1).");
        ReglasPlan.Motivo(RuleFor(c => c.Motivo));
    }
}

/// <summary>
/// La enfermera descarta una sugerencia con motivo obligatorio. Queda registrada (estado Descartado) para medir la tasa de
/// aceptación y no se vuelve a sugerir hasta el fin de la guardia.
/// </summary>
public sealed class DescartarSugerencia(
    IValidator<DescartarSugerenciaComando> validador,
    ContextoPlanCuidados contexto,
    IRepositorioCatalogoNnn catalogo,
    IRepositorioPlanCuidados plan,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    PresentadorPlanCuidados presentador,
    ServicioSugerenciasNnn sugerencias,
    NotificadorPlanCuidados notificador,
    IReloj reloj)
{
    public async Task<Resultado<DiagnosticoPlanDto>> EjecutarAsync(DescartarSugerenciaComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var hospitalizacion = await contexto.HospitalizacionActivaAsync(comando.PacienteId, ct);
        if (!hospitalizacion.EsExito)
        {
            return hospitalizacion.Error;
        }

        var h = hospitalizacion.Valor;
        if ((await catalogo.ObtenerAsync(ct)).Diagnostico(comando.DiagnosticoCodigo) is null)
        {
            return ErroresComunes.ValidacionDeCampo("diagnosticoCodigo", "El diagnóstico no está en el catálogo NANDA-I.");
        }

        if (await plan.ExisteActivoAsync(h.Id, comando.DiagnosticoCodigo!, ct))
        {
            return ErroresNnn.DiagnosticoYaActivo;
        }

        var ahora = reloj.AhoraUtc;
        var pendiente = (await plan.ListarSugerenciasPendientesAsync(h.Id, ct)).FirstOrDefault(s => s.DiagnosticoCodigo == comando.DiagnosticoCodigo);
        var d = DiagnosticoPlan.DescartarSugerencia(
            h.Id, h.PacienteId, comando.DiagnosticoCodigo!, comando.ReglaOrigen!.Trim(), pendiente?.AlertaId,
            pendiente?.EvaluacionId ?? (await evaluaciones.ObtenerUltimaAsync(h.Id, ct))?.Id, MotorSugerenciasNnn.VersionReglas,
            comando.Motivo!, comando.UsuarioId, ahora);
        plan.Agregar(d);
        pendiente?.Descartar(d.Id, ahora);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.DescartarSugerenciaNnn, ahora, comando.UsuarioId, comando.Ip,
            $"Sugerencia descartada {d.Id} de la hospitalización {h.Id}."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        await notificador.PlanActualizadoAsync(h.Id, ct);
        var quedan = await sugerencias.SincronizarAsync(h.Id, h.PacienteId, ct);
        await notificador.SugerenciasAsync(h.Id, quedan.Sugerencias.Count, ct);
        return (await presentador.PresentarAsync(new PlanCuidadosLectura(h.Id, [d], [], [], [], []), ct))[0];
    }
}

/// <param name="Estado"><c>Resuelto</c> o <c>Descartado</c>. Nunca <c>Activo</c>.</param>
public sealed record CambiarEstadoDiagnosticoComando(Guid DiagnosticoId, string? Estado, string? Motivo, Guid UsuarioId, string? Ip);

public sealed class CambiarEstadoDiagnosticoValidador : AbstractValidator<CambiarEstadoDiagnosticoComando>
{
    public CambiarEstadoDiagnosticoValidador()
    {
        RuleFor(c => c.Estado)
            .Must(e => Enumeraciones.TryParseNombre<EstadoDiagnosticoPlan>(e, out var estado) && estado != EstadoDiagnosticoPlan.Activo)
            .WithMessage("El estado debe ser Resuelto o Descartado: un diagnóstico no vuelve a Activo; si reaparece, regístralo de nuevo.");
        ReglasPlan.Motivo(RuleFor(c => c.Motivo));
    }
}

/// <summary>Resuelve o descarta un diagnóstico activo, con motivo. No se puede volver a Activo.</summary>
public sealed class CambiarEstadoDiagnostico(
    IValidator<CambiarEstadoDiagnosticoComando> validador,
    IRepositorioPlanCuidados plan,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    PresentadorPlanCuidados presentador,
    ServicioSugerenciasNnn sugerencias,
    NotificadorPlanCuidados notificador,
    IReloj reloj)
{
    public async Task<Resultado<DiagnosticoPlanDto>> EjecutarAsync(CambiarEstadoDiagnosticoComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var d = await plan.ObtenerDiagnosticoAsync(comando.DiagnosticoId, ct);
        if (d is null)
        {
            return ErroresNnn.DiagnosticoNoEncontrado;
        }

        if (!d.EstaActivo)
        {
            return ErroresNnn.DiagnosticoCerrado;
        }

        Enumeraciones.TryParseNombre<EstadoDiagnosticoPlan>(comando.Estado, out var estado);
        var ahora = reloj.AhoraUtc;
        d.Cerrar(estado, comando.Motivo!, comando.UsuarioId, ahora);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.CerrarDiagnosticoEnfermeria, ahora, comando.UsuarioId, comando.Ip, $"Diagnóstico de enfermería {d.Id}: {estado}."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        await notificador.PlanActualizadoAsync(d.HospitalizacionId, ct);
        var quedan = await sugerencias.SincronizarAsync(d.HospitalizacionId, d.PacienteId, ct);
        await notificador.SugerenciasAsync(d.HospitalizacionId, quedan.Sugerencias.Count, ct);
        var lectura = await plan.ObtenerPlanAsync(d.HospitalizacionId, ct);
        return (await presentador.PresentarAsync(lectura with { Diagnosticos = lectura.Diagnosticos.Where(x => x.Id == d.Id).ToList() }, ct))[0];
    }
}

public sealed record AgregarIntervencionComando(Guid DiagnosticoId, string? Codigo, string? Frecuencia, Guid UsuarioId, string? Ip);

public sealed class AgregarIntervencionValidador : AbstractValidator<AgregarIntervencionComando>
{
    public AgregarIntervencionValidador()
    {
        RuleFor(c => c.Codigo).Must(CodigosNnn.EsIntervencion).WithMessage("Debe ser un código NIC de 4 dígitos.");
        ReglasPlan.Frecuencia(RuleFor(c => c.Frecuencia));
    }
}

/// <summary>
/// Agrega una intervención NIC a un diagnóstico activo. La enfermera agrega cualquiera; el médico solo la NIC 7710
/// (colaboración con el médico): los diagnósticos NANDA-I son de enfermería.
/// </summary>
public sealed class AgregarIntervencion(
    IValidator<AgregarIntervencionComando> validador,
    ContextoPlanCuidados contexto,
    IRepositorioCatalogoNnn catalogo,
    IRepositorioPlanCuidados plan,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    PresentadorPlanCuidados presentador,
    NotificadorPlanCuidados notificador,
    IReloj reloj)
{
    public async Task<Resultado<IntervencionPlanDto>> EjecutarAsync(AgregarIntervencionComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        if (await contexto.ValidarRolAsync(comando.UsuarioId, comando.Codigo!, ct) is { } errorRol)
        {
            return errorRol;
        }

        var d = await plan.ObtenerDiagnosticoAsync(comando.DiagnosticoId, ct);
        if (d is null)
        {
            return ErroresNnn.DiagnosticoNoEncontrado;
        }

        if (!d.EstaActivo)
        {
            return ErroresNnn.DiagnosticoCerrado;
        }

        var c = await catalogo.ObtenerAsync(ct);
        if (c.Intervencion(comando.Codigo) is not { Activo: true })
        {
            return ErroresComunes.ValidacionDeCampo("codigo", "La intervención no está en el catálogo NIC.");
        }

        Enumeraciones.TryParseNombre<FrecuenciaIntervencion>(comando.Frecuencia, out var frecuencia);
        var ahora = reloj.AhoraUtc;
        var i = IntervencionPlan.Planificar(d.Id, comando.Codigo!, frecuencia, comando.UsuarioId, ahora);
        plan.Agregar(i);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.AgregarIntervencionPlan, ahora, comando.UsuarioId, comando.Ip, $"Intervención {i.Id} del diagnóstico de enfermería {d.Id}."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        await notificador.PlanActualizadoAsync(d.HospitalizacionId, ct);
        var lectura = new PlanCuidadosLectura(d.HospitalizacionId, [d], [i], [], [], []);
        return (await presentador.PresentarAsync(lectura, ct))[0].Intervenciones[0];
    }
}

/// <summary>
/// Prepara las ejecuciones de intervenciones del plan (por una alerta o sueltas): comprueba que cada una exista, sea de un
/// diagnóstico activo de la misma hospitalización, siga activa y que el rol pueda registrarla. No guarda: lo hace quien llama.
/// </summary>
public sealed class EjecucionesPlan(IRepositorioPlanCuidados plan, ContextoPlanCuidados contexto, IRepositorioAuditoria auditoria)
{
    public async Task<Resultado<IReadOnlyList<EjecucionIntervencion>>> PrepararAsync(
        IReadOnlyList<Guid> intervencionIds, Guid hospitalizacionId, Guid? alertaId, string? observacion, Guid usuarioId, string? ip,
        DateTime ahora, string campo, CancellationToken ct = default)
    {
        var intervenciones = await plan.ObtenerIntervencionesAsync(intervencionIds, ct);
        var ejecuciones = new List<EjecucionIntervencion>();
        for (var n = 0; n < intervencionIds.Count; n++)
        {
            var i = intervenciones.FirstOrDefault(x => x.Id == intervencionIds[n]);
            var d = i is null ? null : await plan.ObtenerDiagnosticoAsync(i.PlanDiagnosticoId, ct);
            if (i is null || d is null || d.HospitalizacionId != hospitalizacionId)
            {
                return ErroresComunes.ValidacionDeCampo(intervencionIds.Count == 1 && campo == "id" ? campo : $"{campo}[{n}]",
                    "La intervención no existe en el plan de esta hospitalización.");
            }

            if (!d.EstaActivo)
            {
                return ErroresNnn.DiagnosticoCerrado;
            }

            if (!i.Activa)
            {
                return ErroresNnn.IntervencionInactiva;
            }

            if (await contexto.ValidarRolAsync(usuarioId, i.IntervencionCodigo, ct) is { } errorRol)
            {
                return errorRol;
            }

            var e = i.Ejecutar(usuarioId, ahora, alertaId, observacion);
            plan.Agregar(e);
            ejecuciones.Add(e);
            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.RegistrarIntervencion, ahora, usuarioId, ip,
                alertaId is { } a ? $"Ejecución {e.Id} de la intervención {i.Id} por la alerta {a}." : $"Ejecución {e.Id} de la intervención {i.Id}."));
        }

        return ejecuciones;
    }
}

public sealed record RegistrarEjecucionComando(Guid IntervencionId, string? Observacion, Guid? AlertaId, Guid UsuarioId, string? Ip);

public sealed class RegistrarEjecucionValidador : AbstractValidator<RegistrarEjecucionComando>
{
    public RegistrarEjecucionValidador() =>
        RuleFor(c => c.Observacion).Must(o => o is null || o.Trim().Length <= EjecucionIntervencion.LargoMaximoObservacion)
            .WithMessage($"La observación tiene como máximo {EjecucionIntervencion.LargoMaximoObservacion} caracteres.");
}

/// <summary>Registra la ejecución de una intervención (quién, cuándo, por qué alerta y una observación opcional).</summary>
public sealed class RegistrarEjecucion(
    IValidator<RegistrarEjecucionComando> validador,
    ContextoPlanCuidados contexto,
    IRepositorioPlanCuidados plan,
    EjecucionesPlan ejecuciones,
    IUnidadDeTrabajo unidadDeTrabajo,
    DirectorioProfesionales directorio,
    NotificadorPlanCuidados notificador,
    IReloj reloj)
{
    public async Task<Resultado<EjecucionDto>> EjecutarAsync(RegistrarEjecucionComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var intervencion = await plan.ObtenerIntervencionAsync(comando.IntervencionId, ct);
        var d = intervencion is null ? null : await plan.ObtenerDiagnosticoAsync(intervencion.PlanDiagnosticoId, ct);
        if (intervencion is null || d is null)
        {
            return ErroresNnn.IntervencionNoEncontrada;
        }

        if (await contexto.ValidarAlertaAsync(comando.AlertaId, d.HospitalizacionId, "alertaId", ct) is { } errorAlerta)
        {
            return errorAlerta;
        }

        var preparadas = await ejecuciones.PrepararAsync(
            [intervencion.Id], d.HospitalizacionId, comando.AlertaId, comando.Observacion, comando.UsuarioId, comando.Ip, reloj.AhoraUtc, "id", ct);
        if (!preparadas.EsExito)
        {
            return preparadas.Error;
        }

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        await notificador.PlanActualizadoAsync(d.HospitalizacionId, ct);
        return PresentadorPlanCuidados.Ejecucion(await directorio.ObtenerAsync([comando.UsuarioId], ct), preparadas.Valor[0]);
    }
}

public sealed record IndicadorValorado(string? Codigo, int? Puntuacion);

/// <param name="Indicadores">Cada indicador del resultado con su puntuación de 1 a 5; el código del resultado mismo valora el resultado completo.</param>
public sealed record RegistrarValoracionComando(Guid ResultadoId, IReadOnlyList<IndicadorValorado>? Indicadores, Guid UsuarioId, string? Ip);

public sealed class RegistrarValoracionValidador : AbstractValidator<RegistrarValoracionComando>
{
    public RegistrarValoracionValidador()
    {
        RuleFor(c => c.Indicadores)
            .Must(l => l is { Count: > 0 and <= ReglasPlan.MaximoIndicadores })
            .WithMessage($"Valora de 1 a {ReglasPlan.MaximoIndicadores} indicadores.")
            .Must(l => ReglasPlan.SinRepetir(l, i => i?.Codigo)).WithMessage("No repitas indicadores.");
        RuleForEach(c => c.Indicadores).NotNull().ChildRules(i =>
        {
            i.RuleFor(x => x.Codigo).Must(c => CodigosNnn.EsIndicador(c) || CodigosNnn.EsResultado(c))
                .WithMessage("Debe ser un indicador NOC de 6 dígitos o el código del resultado.");
            i.RuleFor(x => x.Puntuacion).Must(p => p is { } v && ValoracionNocSugerida.EsValida(v)).WithMessage("La puntuación va de 1 a 5.");
        });
    }
}

/// <summary>
/// Valora un resultado NOC (escala de 1 a 5) por indicador. Para los indicadores vitales guarda también la puntuación que
/// propuso el motor con los puntos NEWS2 de la última evaluación. No se edita: una valoración mal hecha se corrige con otra.
/// </summary>
public sealed class RegistrarValoracion(
    IValidator<RegistrarValoracionComando> validador,
    IRepositorioPlanCuidados plan,
    IRepositorioCatalogoNnn catalogo,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    DirectorioProfesionales directorio,
    NotificadorPlanCuidados notificador,
    CatalogoTurnos turnos,
    IReloj reloj)
{
    public async Task<Resultado<ValoracionRegistradaDto>> EjecutarAsync(RegistrarValoracionComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var r = await plan.ObtenerResultadoAsync(comando.ResultadoId, ct);
        var d = r is null ? null : await plan.ObtenerDiagnosticoAsync(r.PlanDiagnosticoId, ct);
        if (r is null || d is null)
        {
            return ErroresNnn.ResultadoNoEncontrado;
        }

        if (!d.EstaActivo)
        {
            return ErroresNnn.DiagnosticoCerrado;
        }

        var c = await catalogo.ObtenerAsync(ct);
        var evaluacion = await evaluaciones.ObtenerUltimaAsync(d.HospitalizacionId, ct);
        var ahora = reloj.AhoraUtc;
        var turno = Guardias.Clave(Guardias.Actual(turnos, ahora).Turno);
        var valoraciones = new List<ValoracionResultado>();
        for (var n = 0; n < comando.Indicadores!.Count; n++)
        {
            var solicitado = comando.Indicadores[n];
            IndicadorNoc? indicador = null;
            if (solicitado.Codigo != r.ResultadoCodigo)
            {
                indicador = c.Indicador(solicitado.Codigo);
                if (indicador is not { Activo: true } || indicador.ResultadoCodigo != r.ResultadoCodigo)
                {
                    return ErroresComunes.ValidacionDeCampo($"indicadores[{n}].codigo", "El indicador no es de este resultado NOC.");
                }
            }

            var sugerida = indicador?.ParametroVital is { } p ? ValoracionNocSugerida.Para(p, evaluacion) : null;
            valoraciones.Add(ValoracionResultado.Registrar(r.Id, indicador?.Codigo, solicitado.Puntuacion!.Value, sugerida, comando.UsuarioId, ahora, turno));
        }

        valoraciones.ForEach(plan.Agregar);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.ValorarResultado, ahora, comando.UsuarioId, comando.Ip,
            $"Valoración del resultado NOC {r.Id} del diagnóstico de enfermería {d.Id} ({valoraciones.Count} indicadores)."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        await notificador.PlanActualizadoAsync(d.HospitalizacionId, ct);
        var personas = await directorio.ObtenerAsync([comando.UsuarioId], ct);
        return new ValoracionRegistradaDto(r.Id, ahora, turno, valoraciones.Select(v => PresentadorPlanCuidados.Valoracion(c, personas, r, v)).ToList());
    }
}

public sealed record ObtenerValoracionSugeridaConsulta(Guid ResultadoId, Guid UsuarioId, string? Ip);

/// <summary>Los indicadores de un resultado con la puntuación que propone el motor, para que la app la muestre antes de valorar.</summary>
public sealed class ObtenerValoracionSugerida(
    IRepositorioPlanCuidados plan, IRepositorioCatalogoNnn catalogo, IRepositorioEvaluaciones evaluaciones, AuditoriaHistorial auditoria)
{
    public async Task<Resultado<ValoracionSugeridaDto>> EjecutarAsync(ObtenerValoracionSugeridaConsulta consulta, CancellationToken ct = default)
    {
        var r = await plan.ObtenerResultadoAsync(consulta.ResultadoId, ct);
        var d = r is null ? null : await plan.ObtenerDiagnosticoAsync(r.PlanDiagnosticoId, ct);
        if (r is null || d is null)
        {
            return ErroresNnn.ResultadoNoEncontrado;
        }

        var c = await catalogo.ObtenerAsync(ct);
        var evaluacion = await evaluaciones.ObtenerUltimaAsync(d.HospitalizacionId, ct);
        var anteriores = (await plan.ObtenerPlanAsync(d.HospitalizacionId, ct)).ValoracionesDe(r.Id);
        int? Ultima(string? indicador) => anteriores.LastOrDefault(v => v.IndicadorCodigo == indicador)?.Puntuacion;

        var noc = c.Resultado(r.ResultadoCodigo);
        var indicadores = c.IndicadoresDe(r.ResultadoCodigo)
            .Select(i => new IndicadorSugeridoDto(
                i.Codigo, i.Etiqueta, TextosNnn.De(i.ParametroVital),
                i.ParametroVital is { } p ? ValoracionNocSugerida.Para(p, evaluacion) : null, Ultima(i.Codigo)))
            .Append(new IndicadorSugeridoDto(r.ResultadoCodigo, $"{noc?.Etiqueta ?? r.ResultadoCodigo} (global)", null, null, Ultima(null)))
            .ToList();

        var auditado = await auditoria.RegistrarAsync(d.PacienteId, d.HospitalizacionId, "valoracion-sugerida", consulta.UsuarioId, consulta.Ip, ct);
        return auditado.EsExito
            ? new ValoracionSugeridaDto(r.Id, r.ResultadoCodigo, noc?.Etiqueta ?? r.ResultadoCodigo, noc?.Escala ?? string.Empty,
                evaluacion?.Id, evaluacion?.EvaluadaEn, indicadores)
            : auditado.Error;
    }
}

public sealed record ObtenerPlanCuidadosConsulta(Guid PacienteId, Guid? HospitalizacionId, Guid UsuarioId, string? Ip);

/// <summary>El plan completo de un episodio (por defecto el activo o el último). Leerlo cuenta como consulta del historial.</summary>
public sealed class ObtenerPlanCuidados(
    EpisodiosPaciente episodios, IRepositorioPlanCuidados plan, PresentadorPlanCuidados presentador, AuditoriaHistorial auditoria)
{
    public async Task<Resultado<PlanCuidadosDto>> EjecutarAsync(ObtenerPlanCuidadosConsulta consulta, CancellationToken ct = default)
    {
        var elegido = await episodios.ElegirAsync(consulta.PacienteId, consulta.HospitalizacionId, ct);
        if (!elegido.EsExito)
        {
            return elegido.Error;
        }

        var h = elegido.Valor.Hospitalizacion;
        var diagnosticos = await presentador.PresentarAsync(await plan.ObtenerPlanAsync(h.Id, ct), ct);
        var auditado = await auditoria.RegistrarAsync(consulta.PacienteId, h.Id, "plan-cuidados", consulta.UsuarioId, consulta.Ip, ct);
        return auditado.EsExito
            ? new PlanCuidadosDto(consulta.PacienteId, h.Id, h.EstaActiva, MotorSugerenciasNnn.VersionReglas, diagnosticos)
            : auditado.Error;
    }
}

/// <summary>
/// Al egresar: los diagnósticos activos pasan a Resuelto con el motivo "Egreso" y las sugerencias pendientes expiran. Se
/// prepara dentro de la misma unidad de trabajo del egreso.
/// </summary>
public sealed class CierrePlanCuidados(IRepositorioPlanCuidados plan, IRepositorioAuditoria auditoria)
{
    public async Task<int> PrepararAlEgresarAsync(Guid hospitalizacionId, Guid usuarioId, string? ip, DateTime ahora, CancellationToken ct = default)
    {
        var activos = await plan.ListarActivosParaCerrarAsync(hospitalizacionId, ct);
        foreach (var d in activos)
        {
            d.ResolverPorEgreso(usuarioId, ahora);
            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.CerrarDiagnosticoEnfermeria, ahora, usuarioId, ip,
                $"Diagnóstico de enfermería {d.Id}: Resuelto por el egreso de la hospitalización {hospitalizacionId}."));
        }

        foreach (var s in await plan.ListarSugerenciasPendientesAsync(hospitalizacionId, ct))
        {
            s.Expirar(ahora);
        }

        return activos.Count;
    }
}

/// <summary>El plan en el resumen del paciente: diagnósticos activos con su última valoración NOC y sugerencias pendientes.</summary>
public sealed class ResumenPlanCuidados(IRepositorioPlanCuidados plan, IRepositorioCatalogoNnn catalogo, ServicioSugerenciasNnn sugerencias)
{
    public async Task<PlanCuidadosResumenDto> ObtenerAsync(Hospitalizacion h, CancellationToken ct = default)
    {
        var lectura = await plan.ObtenerPlanAsync(h.Id, ct);
        var c = await catalogo.ObtenerAsync(ct);
        var activos = lectura.Diagnosticos.Where(d => d.EstaActivo).Select(d =>
        {
            var ultima = lectura.ResultadosDe(d.Id)
                .Select(r => (r, u: PresentadorPlanCuidados.Ultima(lectura.ValoracionesDe(r.Id))))
                .Where(x => x.u is not null)
                .OrderByDescending(x => x.u!.Value.En)
                .Select(x => new UltimaValoracionDto(
                    x.r.ResultadoCodigo, c.Etiqueta(TaxonomiaNnn.Noc, x.r.ResultadoCodigo), x.u!.Value.Puntuacion, x.r.PuntuacionMeta, x.u.Value.En))
                .FirstOrDefault();
            return new DiagnosticoResumenDto(d.Id, d.DiagnosticoCodigo, c.Etiqueta(TaxonomiaNnn.Nanda, d.DiagnosticoCodigo), d.ConfirmadoEn, ultima);
        }).ToList();

        var pendientes = h.EstaActiva ? (await sugerencias.CalcularAsync(h.Id, ct)).Sugerencias.Count : 0;
        return new PlanCuidadosResumenDto(activos, pendientes);
    }
}

/// <summary>
/// El plan en la entrega de turno: por hospitalización, los diagnósticos activos, las intervenciones <c>CadaTurno</c> que no
/// se ejecutaron en la guardia y los resultados NOC que no se valoraron en la guardia.
/// </summary>
public sealed class PlanEnEntregaTurno(IRepositorioPlanCuidados plan, IRepositorioCatalogoNnn catalogo)
{
    public async Task<IReadOnlyDictionary<Guid, PlanEntregaDto>> ObtenerAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, DateTime inicioGuardia, CancellationToken ct = default)
    {
        var planes = await plan.ObtenerPlanesActivosAsync(hospitalizaciones, ct);
        var c = await catalogo.ObtenerAsync(ct);
        return planes.ToDictionary(p => p.Key, p =>
        {
            var l = p.Value;
            var codigoDe = l.Diagnosticos.ToDictionary(d => d.Id, d => d.DiagnosticoCodigo);
            return new PlanEntregaDto(
                l.Diagnosticos.Select(d => new DiagnosticoEntregaDto(d.Id, d.DiagnosticoCodigo, c.Etiqueta(TaxonomiaNnn.Nanda, d.DiagnosticoCodigo))).ToList(),
                l.Intervenciones
                    .Where(i => i.Activa && i.Frecuencia == FrecuenciaIntervencion.CadaTurno && !l.EjecucionesDe(i.Id).Any(e => e.EjecutadaEn >= inicioGuardia))
                    .Select(i => new IntervencionPendienteDto(i.Id, i.PlanDiagnosticoId, i.IntervencionCodigo,
                        c.Etiqueta(TaxonomiaNnn.Nic, i.IntervencionCodigo), codigoDe[i.PlanDiagnosticoId]))
                    .ToList(),
                l.Resultados
                    .Where(r => !l.ValoracionesDe(r.Id).Any(v => v.ValoradaEn >= inicioGuardia))
                    .Select(r => new ValoracionPendienteDto(r.Id, r.PlanDiagnosticoId, r.ResultadoCodigo,
                        c.Etiqueta(TaxonomiaNnn.Noc, r.ResultadoCodigo), codigoDe[r.PlanDiagnosticoId]))
                    .ToList());
        });
    }
}
