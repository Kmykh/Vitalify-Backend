using FluentValidation;
using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Historial;
using Vitalify.Application.Puertos;
using Vitalify.Application.Turnos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Nnn;
using Vitalify.Domain.Turnos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Nnn;

// Fase 9: automatizar el trabajo del PAE, no la decisión. El sistema precarga, agenda, recuerda, registra lo que ya pasó
// (la 6680 de una toma completa) y propone; la enfermera confirma, ejecuta, valora y cierra (docs/nnn.md).

/// <summary>Con qué se arma la basal de un paquete: las evaluaciones de los últimos 30 min, el dolor vigente y la escala.</summary>
public sealed record DatosPaquete(IReadOnlyList<EvaluacionRiesgo> Recientes, int? Dolor, EscalaSpo2 Escala);

/// <summary>
/// Arma el "paquete listo" de cada sugerencia con <see cref="ProtocoloNnn"/>: las NIC con su frecuencia por protocolo y los
/// NOC con la basal propuesta (mediana de los puntos NEWS2 de los últimos 30 min, o el dolor) y la meta por defecto.
/// </summary>
public sealed class ServicioPaquetesNnn(IRepositorioEvaluaciones evaluaciones, IRepositorioObservaciones observaciones, OpcionesClinicas opciones, IReloj reloj)
{
    public async Task<DatosPaquete> DatosAsync(Guid hospitalizacionId, CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var ultima = await evaluaciones.ObtenerUltimaAsync(hospitalizacionId, ct);
        var recientes = (await evaluaciones.ListarAsync(hospitalizacionId, ahora - ProtocoloNnn.VentanaBasal, ahora, ct)).ToList();
        if (recientes.Count == 0 && ultima is not null)
        {
            recientes.Add(ultima);
        }

        var toma = await observaciones.ObtenerUltimaTomaAsync(hospitalizacionId, ct);
        var dolor = toma is not null && FrecuenciaTomaManual.Cuenta(toma, ahora, ultima, opciones.ToleranciaToma) ? toma.Contexto?.Dolor : null;
        return new DatosPaquete(recientes, dolor, ultima?.EscalaSpo2 ?? EscalaSpo2.Escala1);
    }

    public static PaqueteNnnDto Armar(CatalogoNnnLectura c, SugerenciaNnn s, DatosPaquete datos)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(datos);
        var intervenciones = s.Intervenciones
            .Select(i => new PaqueteIntervencionDto(i, c.Etiqueta(TaxonomiaNnn.Nic, i), c.Intervencion(i)?.Verificado ?? false,
                ProtocoloNnn.FrecuenciaSugerida(i).ToString()))
            .ToList();
        var resultados = s.Resultados.Select(r =>
        {
            var desdeSignos = ProtocoloNnn.PuntuacionDesdeSignos(r, datos.Recientes, datos.Dolor);
            var basal = desdeSignos ?? ProtocoloNnn.BasalPorDefecto;
            var tope = datos.Escala == EscalaSpo2.Escala2 && r is "0402" or "0415";
            return new PaqueteResultadoDto(r, c.Etiqueta(TaxonomiaNnn.Noc, r), c.Resultado(r)?.Verificado ?? false, basal,
                ProtocoloNnn.MetaSugerida(basal, tope), desdeSignos is not null);
        }).ToList();
        return new PaqueteNnnDto(intervenciones, resultados, ProtocoloNnn.Version);
    }

    /// <summary>¿Lo que se confirma es exactamente el paquete? (mismas NIC con la misma frecuencia y mismos NOC con la misma basal y meta).</summary>
    public static bool Coincide(PaqueteNnnDto paquete, IReadOnlyList<IntervencionSolicitada> intervenciones, IReadOnlyList<ResultadoSolicitado> resultados)
    {
        ArgumentNullException.ThrowIfNull(paquete);
        var nic = paquete.Intervenciones.Select(i => $"{i.Codigo}:{i.Frecuencia}").Order(StringComparer.Ordinal);
        var nicPedidas = intervenciones.Select(i => $"{i.Codigo}:{i.Frecuencia}").Order(StringComparer.Ordinal);
        var noc = paquete.Resultados.Select(r => $"{r.Codigo}:{r.PuntuacionBasal}:{r.PuntuacionMeta}").Order(StringComparer.Ordinal);
        var nocPedidos = resultados.Select(r => $"{r.Codigo}:{r.PuntuacionBasal}:{r.PuntuacionMeta}").Order(StringComparer.Ordinal);
        return nic.SequenceEqual(nicPedidas, StringComparer.OrdinalIgnoreCase) && noc.SequenceEqual(nocPedidos, StringComparer.Ordinal);
    }
}

public sealed record ConfirmarPaqueteComando(Guid PacienteId, string? DiagnosticoCodigo, Guid UsuarioId, string? Ip);

/// <summary>
/// "Confirmar" con un toque: la enfermera acepta la sugerencia tal como viene en su paquete (NIC con frecuencia por protocolo
/// y NOC con basal y meta). Queda como <c>confirmadoSinCambios</c>, para medir el sesgo de automatización. Si quiere
/// cambiar algo, usa la confirmación completa (<c>POST .../plan-cuidados/diagnosticos</c>).
/// </summary>
public sealed class ConfirmarPaqueteSugerido(
    ContextoPlanCuidados contexto,
    ServicioSugerenciasNnn sugerencias,
    ServicioPaquetesNnn paquetes,
    IRepositorioCatalogoNnn catalogo,
    ConfirmarDiagnostico confirmar)
{
    public async Task<Resultado<DiagnosticoPlanDto>> EjecutarAsync(ConfirmarPaqueteComando comando, CancellationToken ct = default)
    {
        if (!CodigosNnn.EsDiagnostico(comando.DiagnosticoCodigo))
        {
            return ErroresComunes.ValidacionDeCampo("diagnosticoCodigo", "Debe ser un código NANDA-I de 5 dígitos (por ejemplo 00032).");
        }

        var hospitalizacion = await contexto.HospitalizacionActivaAsync(comando.PacienteId, ct);
        if (!hospitalizacion.EsExito)
        {
            return hospitalizacion.Error;
        }

        var h = hospitalizacion.Valor;
        var s = (await sugerencias.CalcularAsync(h.Id, ct)).Sugerencias.FirstOrDefault(x => x.DiagnosticoCodigo == comando.DiagnosticoCodigo);
        if (s is null)
        {
            return ErroresNnn.SugerenciaNoEncontrada;
        }

        var paquete = ServicioPaquetesNnn.Armar(await catalogo.ObtenerAsync(ct), s, await paquetes.DatosAsync(h.Id, ct));
        return await confirmar.EjecutarAsync(new ConfirmarDiagnosticoComando(
            comando.PacienteId, s.DiagnosticoCodigo, nameof(OrigenDiagnostico.Sugerido), s.Regla, s.AlertaId,
            paquete.Intervenciones.Select(i => new IntervencionSolicitada(i.Codigo, i.Frecuencia)).ToList(),
            paquete.Resultados.Select(r => new ResultadoSolicitado(r.Codigo, r.PuntuacionBasal, r.PuntuacionMeta)).ToList(),
            comando.UsuarioId, comando.Ip, PaqueteSinCambios: true), ct);
    }
}

/// <summary>La agenda de una hospitalización: el nivel NEWS2 con que se calculó, la última evaluación y cada intervención.</summary>
public sealed record AgendaHospitalizacion(NivelRespuestaNews2 Nivel, EvaluacionRiesgo? Ultima, IReadOnlyDictionary<Guid, ItemAgenda> Items)
{
    public int Vencidas => Items.Values.Count(i => i.Estado == EstadoAgenda.Vencida);

    public int Proximas => Items.Values.Count(i => i.Estado == EstadoAgenda.Proxima);
}

/// <summary>
/// Calcula la agenda de las intervenciones activas (<see cref="AgendaIntervenciones"/>) con el nivel NEWS2 efectivo: el
/// mayor desde la última toma manual completa (sube al instante; baja solo con una toma completa en el nivel inferior).
/// </summary>
public sealed class ServicioAgendaPlan(IRepositorioEvaluaciones evaluaciones, IRepositorioObservaciones observaciones, CatalogoTurnos turnos, IReloj reloj)
{
    public async Task<AgendaHospitalizacion> CalcularAsync(PlanCuidadosLectura plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var ahora = reloj.AhoraUtc;
        var h = plan.HospitalizacionId;
        var ultima = await evaluaciones.ObtenerUltimaAsync(h, ct);
        var tomas = await observaciones.ListarDesdeAsync(h, ahora - FrecuenciaTomaManual.TopeVigencia, ct);
        var completa = tomas.FirstOrDefault(t => t.Pas is not null && t.Conciencia is not null);
        var desde = completa is null ? ahora - FrecuenciaTomaManual.TopeVigencia : completa.RegistradaEn.AddSeconds(-1);
        var recientes = await evaluaciones.ListarAsync(h, desde, ahora, ct);
        var nivel = ProtocoloNnn.NivelEfectivo(recientes.Count > 0 ? recientes : ultima is null ? [] : [ultima]);
        var guardia = Guardias.Actual(turnos, ahora).Intervalo;
        var activos = plan.Diagnosticos.Where(d => d.EstaActivo).Select(d => d.Id).ToHashSet();
        var items = plan.Intervenciones
            .Where(i => i.Activa && activos.Contains(i.PlanDiagnosticoId))
            .ToDictionary(i => i.Id, i => AgendaIntervenciones.Calcular(
                i, plan.EjecucionesDe(i.Id).LastOrDefault()?.EjecutadaEn, nivel, guardia, ultima?.ProximaTomaManualEn, ahora));
        return new AgendaHospitalizacion(nivel, ultima, items);
    }
}

/// <summary>Qué diagnósticos activos están "listos para resolver" (<see cref="CriterioResolucion"/>), con el motivo precargado.</summary>
public sealed class ServicioResolucionPlan(
    IRepositorioEvaluaciones evaluaciones, IRepositorioObservaciones observaciones, IRepositorioAlertas alertas, IReloj reloj)
{
    public async Task<IReadOnlyDictionary<Guid, string>> CalcularAsync(PlanCuidadosLectura plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var candidatos = plan.Diagnosticos.Where(d => d.EstaActivo && d.ReglaOrigen is not null).ToList();
        if (candidatos.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ahora = reloj.AhoraUtc;
        var desde = ahora - CriterioResolucion.VentanaFiebre;
        var h = plan.HospitalizacionId;
        var evals = await evaluaciones.ListarAsync(h, desde, ahora, ct);
        var tomas = await observaciones.ListarAsync(h, desde, ahora, ct);
        var riesgoAbierta = (await alertas.ListarDeHospitalizacionAsync(h, ct)).Any(a => a.Alerta.EstaAbierta && a.Alerta.Tipo == TipoAlerta.Riesgo);

        var listos = new Dictionary<Guid, string>();
        foreach (var d in candidatos)
        {
            var resultados = plan.ResultadosDe(d.Id)
                .Select(r => new EstadoResultadoNoc(PresentadorPlanCuidados.Ultima(plan.ValoracionesDe(r.Id))?.Puntuacion, r.PuntuacionMeta))
                .ToList();
            if (CriterioResolucion.ListoParaResolver(new DatosResolucion(d, evals, tomas, resultados, riesgoAbierta, ahora)) is { } motivo)
            {
                listos[d.Id] = motivo;
            }
        }

        return listos;
    }
}

/// <summary>
/// Una toma manual <b>completa</b> (presión y conciencia) de una enfermera registra sola la NIC 6680 (monitorización de signos
/// vitales) si está en el plan: es un hecho que ella ya realizó, y queda a su nombre, a la hora de la toma y con origen
/// <c>TomaManual</c>. Nunca otra NIC (Fable: ni 3350, ni 3320, ni 7710).
/// </summary>
public sealed class EjecucionSignosAlRegistrarToma(
    IRepositorioPlanCuidados plan,
    IRepositorioUsuarios usuarios,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    NotificadorPlanCuidados notificador,
    IReloj reloj) : IManejadorObservacionRegistrada
{
    public async Task ManejarAsync(ObservacionRegistrada evento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evento);
        if (!evento.Completa || (await usuarios.ObtenerPorIdAsync(evento.RegistradaPor, ct))?.Rol != Rol.Enfermera)
        {
            return;
        }

        var lectura = await plan.ObtenerPlanAsync(evento.HospitalizacionId, ct);
        var activos = lectura.Diagnosticos.Where(d => d.EstaActivo).Select(d => d.Id).ToHashSet();
        var ids = lectura.Intervenciones
            .Where(i => i.Activa && i.IntervencionCodigo == ProtocoloNnn.NicSignosVitales && activos.Contains(i.PlanDiagnosticoId))
            .Select(i => i.Id)
            .ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var ahora = reloj.AhoraUtc;
        foreach (var i in await plan.ObtenerIntervencionesAsync(ids, ct))
        {
            var e = i.Ejecutar(evento.RegistradaPor, evento.ObservadaEn, null, null, OrigenEjecucion.TomaManual);
            plan.Agregar(e);
            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.RegistrarIntervencion, ahora, evento.RegistradaPor, null,
                $"Ejecución {e.Id} de la intervención {i.Id} por la toma manual {evento.ObservacionId}."));
        }

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (guardado.EsExito)
        {
            await notificador.PlanActualizadoAsync(evento.HospitalizacionId, ct);
        }
    }
}

/// <param name="Cambio">La propuesta difiere de la última valoración: la app lo resalta.</param>
public sealed record IndicadorTurnoDto(string Codigo, string Etiqueta, string? ParametroVital, int? PuntuacionSugerida, int? UltimaPuntuacion, bool Cambio);

public sealed record ResultadoTurnoDto(
    Guid PlanResultadoId, Guid PlanDiagnosticoId, string DiagnosticoCodigo, string DiagnosticoEtiqueta, string Codigo, string Etiqueta,
    int PuntuacionBasal, int PuntuacionMeta, bool ValoradoEnGuardia, IReadOnlyList<IndicadorTurnoDto> Indicadores);

/// <summary>La valoración NOC del turno de un paciente: cada resultado activo con lo que propone el motor, para confirmar en lote.</summary>
public sealed record ValoracionTurnoDto(
    Guid PacienteId, Guid HospitalizacionId, string? Guardia, Guid? EvaluacionId, DateTime? EvaluadaEn, bool? EvaluacionCompleta,
    IReadOnlyList<ResultadoTurnoDto> Resultados, string Aviso);

public sealed record ObtenerValoracionTurnoConsulta(Guid PacienteId, Guid UsuarioId, string? Ip);

/// <summary>
/// Lo que la enfermera valora al cierre del turno, en una sola pantalla: los NOC de los diagnósticos activos con la puntuación
/// propuesta (mediana de los últimos 30 min, o el dolor) premarcada y resaltando lo que cambió desde la última valoración.
/// Leerlo cuenta como consulta del historial.
/// </summary>
public sealed class ObtenerValoracionTurno(
    ContextoPlanCuidados contexto,
    IRepositorioPlanCuidados plan,
    IRepositorioCatalogoNnn catalogo,
    ServicioPaquetesNnn paquetes,
    AuditoriaHistorial auditoria,
    CatalogoTurnos turnos,
    IReloj reloj)
{
    public async Task<Resultado<ValoracionTurnoDto>> EjecutarAsync(ObtenerValoracionTurnoConsulta consulta, CancellationToken ct = default)
    {
        var hospitalizacion = await contexto.HospitalizacionActivaAsync(consulta.PacienteId, ct);
        if (!hospitalizacion.EsExito)
        {
            return hospitalizacion.Error;
        }

        var h = hospitalizacion.Valor;
        var lectura = await plan.ObtenerPlanAsync(h.Id, ct);
        var c = await catalogo.ObtenerAsync(ct);
        var datos = await paquetes.DatosAsync(h.Id, ct);
        var (turno, intervalo) = Guardias.Actual(turnos, reloj.AhoraUtc);
        var resultados = lectura.Diagnosticos.Where(d => d.EstaActivo)
            .SelectMany(d => lectura.ResultadosDe(d.Id).Select(r => Resultado(c, lectura, d, r, datos, intervalo.InicioUtc)))
            .ToList();

        var auditado = await auditoria.RegistrarAsync(consulta.PacienteId, h.Id, "valoracion-turno", consulta.UsuarioId, consulta.Ip, ct);
        if (!auditado.EsExito)
        {
            return auditado.Error;
        }

        var ultima = datos.Recientes.Count == 0 ? null : datos.Recientes.MaxBy(e => e.EvaluadaEn);
        return new ValoracionTurnoDto(consulta.PacienteId, h.Id, Guardias.Clave(turno), ultima?.Id, ultima?.EvaluadaEn,
            ultima is null ? null : ultima.News2Completo && ultima.MewsCompleto, resultados, TextosNnn.Aviso);
    }

    /// <summary>La puntuación que se propone para un indicador vital (mediana de 30 min) o para el resultado completo.</summary>
    public static int? Propuesta(IndicadorNoc? indicador, string resultadoCodigo, DatosPaquete datos) =>
        indicador?.ParametroVital is { } p
            ? ValoracionNocSugerida.DesdePuntosNews2(PrecisionSensor.Mediana(datos.Recientes.Select(e =>
                e.News2().Puntos.TryGetValue(Parametro(p), out var x) ? (int?)x : null)))
            : indicador is null ? ProtocoloNnn.PuntuacionDesdeSignos(resultadoCodigo, datos.Recientes, datos.Dolor) : null;

    private static ResultadoTurnoDto Resultado(
        CatalogoNnnLectura c, PlanCuidadosLectura lectura, DiagnosticoPlan d, ResultadoPlan r, DatosPaquete datos, DateTime inicioGuardia)
    {
        var anteriores = lectura.ValoracionesDe(r.Id);
        int? Ultima(string? indicador) => anteriores.LastOrDefault(v => v.IndicadorCodigo == indicador)?.Puntuacion;
        IndicadorTurnoDto Fila(string codigo, string etiqueta, string? parametro, int? sugerida, int? ultima) =>
            new(codigo, etiqueta, parametro, sugerida, ultima, sugerida is not null && ultima is not null && sugerida != ultima);

        var indicadores = c.IndicadoresDe(r.ResultadoCodigo)
            .Select(i => Fila(i.Codigo, i.Etiqueta, TextosNnn.De(i.ParametroVital), Propuesta(i, r.ResultadoCodigo, datos), Ultima(i.Codigo)))
            .Append(Fila(r.ResultadoCodigo, $"{c.Etiqueta(TaxonomiaNnn.Noc, r.ResultadoCodigo)} (global)", null,
                Propuesta(null, r.ResultadoCodigo, datos), Ultima(null)))
            .ToList();
        return new ResultadoTurnoDto(r.Id, d.Id, d.DiagnosticoCodigo, c.Etiqueta(TaxonomiaNnn.Nanda, d.DiagnosticoCodigo), r.ResultadoCodigo,
            c.Etiqueta(TaxonomiaNnn.Noc, r.ResultadoCodigo), r.PuntuacionBasal, r.PuntuacionMeta,
            anteriores.Any(v => v.ValoradaEn >= inicioGuardia), indicadores);
    }

    private static ParametroClinico Parametro(ParametroVital p) => p switch
    {
        ParametroVital.Fr => ParametroClinico.Fr,
        ParametroVital.Spo2 => ParametroClinico.Spo2,
        ParametroVital.Temperatura => ParametroClinico.Temperatura,
        ParametroVital.Pas => ParametroClinico.Pas,
        ParametroVital.Fc => ParametroClinico.Fc,
        _ => ParametroClinico.Conciencia,
    };
}

public sealed record ResultadoValoradoTurno(Guid? ResultadoId, IReadOnlyList<IndicadorValorado>? Indicadores);

public sealed record RegistrarValoracionTurnoComando(Guid PacienteId, IReadOnlyList<ResultadoValoradoTurno>? Resultados, Guid UsuarioId, string? Ip);

public sealed class RegistrarValoracionTurnoValidador : AbstractValidator<RegistrarValoracionTurnoComando>
{
    public RegistrarValoracionTurnoValidador()
    {
        RuleFor(c => c.Resultados)
            .Must(l => l is { Count: > 0 and <= ReglasPlan.MaximoElementos * ReglasPlan.MaximoElementos })
            .WithMessage("Valora al menos un resultado NOC.")
            .Must(l => l is null || l.Select(r => r?.ResultadoId).Distinct().Count() == l.Count).WithMessage("No repitas resultados.");
        RuleForEach(c => c.Resultados).NotNull().ChildRules(r =>
        {
            r.RuleFor(x => x.ResultadoId).NotNull().WithMessage("Indica el resultado NOC.");
            r.RuleFor(x => x.Indicadores)
                .Must(l => l is { Count: > 0 and <= ReglasPlan.MaximoIndicadores })
                .WithMessage($"Valora de 1 a {ReglasPlan.MaximoIndicadores} indicadores.")
                .Must(l => ReglasPlan.SinRepetir(l, i => i?.Codigo)).WithMessage("No repitas indicadores.");
            r.RuleForEach(x => x.Indicadores).NotNull().ChildRules(i =>
            {
                i.RuleFor(x => x.Codigo).Must(c => CodigosNnn.EsIndicador(c) || CodigosNnn.EsResultado(c))
                    .WithMessage("Debe ser un indicador NOC de 6 dígitos o el código del resultado.");
                i.RuleFor(x => x.Puntuacion).Must(p => p is { } v && ValoracionNocSugerida.EsValida(v)).WithMessage("La puntuación va de 1 a 5.");
            });
        });
    }
}

/// <summary>
/// Guarda en una sola operación la valoración del turno de un paciente (todos sus NOC). Cada valoración guarda también la
/// puntuación que propuso el motor, para medir cuánto la cambia la enfermera. Es por paciente: nunca "confirmar todo" entre
/// pacientes (Fable). No se edita: una valoración mal hecha se corrige con otra.
/// </summary>
public sealed class RegistrarValoracionTurno(
    IValidator<RegistrarValoracionTurnoComando> validador,
    ContextoPlanCuidados contexto,
    IRepositorioPlanCuidados plan,
    IRepositorioCatalogoNnn catalogo,
    ServicioPaquetesNnn paquetes,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    NotificadorPlanCuidados notificador,
    CatalogoTurnos turnos,
    IReloj reloj)
{
    public async Task<Resultado<int>> EjecutarAsync(RegistrarValoracionTurnoComando comando, CancellationToken ct = default)
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
        var lectura = await plan.ObtenerPlanAsync(h.Id, ct);
        var c = await catalogo.ObtenerAsync(ct);
        var datos = await paquetes.DatosAsync(h.Id, ct);
        var ahora = reloj.AhoraUtc;
        var turno = Guardias.Clave(Guardias.Actual(turnos, ahora).Turno);
        var valoraciones = new List<ValoracionResultado>();
        for (var n = 0; n < comando.Resultados!.Count; n++)
        {
            var pedido = comando.Resultados[n];
            var r = lectura.Resultados.FirstOrDefault(x => x.Id == pedido.ResultadoId);
            if (r is null || lectura.Diagnosticos.FirstOrDefault(d => d.Id == r.PlanDiagnosticoId) is not { EstaActivo: true } d)
            {
                return ErroresComunes.ValidacionDeCampo($"resultados[{n}].resultadoId", "El resultado no es de un diagnóstico activo de este paciente.");
            }

            for (var m = 0; m < pedido.Indicadores!.Count; m++)
            {
                var solicitado = pedido.Indicadores[m];
                IndicadorNoc? indicador = null;
                if (solicitado.Codigo != r.ResultadoCodigo)
                {
                    indicador = c.Indicador(solicitado.Codigo);
                    if (indicador is not { Activo: true } || indicador.ResultadoCodigo != r.ResultadoCodigo)
                    {
                        return ErroresComunes.ValidacionDeCampo($"resultados[{n}].indicadores[{m}].codigo", "El indicador no es de este resultado NOC.");
                    }
                }

                valoraciones.Add(ValoracionResultado.Registrar(r.Id, indicador?.Codigo, solicitado.Puntuacion!.Value,
                    ObtenerValoracionTurno.Propuesta(indicador, r.ResultadoCodigo, datos), comando.UsuarioId, ahora, turno));
            }

            auditoria.Agregar(RegistroAuditoria.Registrar(
                AccionAuditoria.ValorarResultado, ahora, comando.UsuarioId, comando.Ip,
                $"Valoración del resultado NOC {r.Id} del diagnóstico de enfermería {d.Id} ({pedido.Indicadores.Count} indicadores, en lote)."));
        }

        valoraciones.ForEach(plan.Agregar);
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        await notificador.PlanActualizadoAsync(h.Id, ct);
        return valoraciones.Count;
    }
}
