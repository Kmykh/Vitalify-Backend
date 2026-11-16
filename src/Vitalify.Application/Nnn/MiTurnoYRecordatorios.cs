using System.Collections.Concurrent;
using Vitalify.Application.Alertas;
using Vitalify.Application.Comun;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Application.Turnos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Nnn;
using Vitalify.Domain.Turnos;

namespace Vitalify.Application.Nnn;

public sealed record PendientePlanDto(Guid IntervencionId, Guid PlanDiagnosticoId, string Codigo, string Etiqueta, DateTime? ProximaEn, string Agenda);

public sealed record AlertaTurnoDto(Guid Id, string Tipo, string Prioridad, DateTime CreadaEn);

/// <param name="NivelRespuesta">Nivel de respuesta NEWS2 con que se agenda (<c>cero</c> … <c>alto</c>).</param>
public sealed record PacientePanelTurnoDto(
    Guid PacienteId, Guid HospitalizacionId, string NombreCompleto, string Cama, int? News2, bool? EvaluacionCompleta, string NivelRespuesta,
    AlertaTurnoDto? Alerta, IReadOnlyList<PendientePlanDto> Vencidas, IReadOnlyList<PendientePlanDto> Proximas, int SugerenciasPendientes,
    int ValoracionesPendientes, bool NotaTurnoFirmada);

/// <summary>
/// La primera pantalla de la enfermera: sus pacientes ordenados por riesgo y, por paciente, la alerta abierta, lo vencido, lo
/// próximo, las sugerencias por decidir, los NOC por valorar en la guardia y si ya firmó la nota del turno.
/// </summary>
public sealed record PanelTurnoDto(
    Guid ServicioId, string? Guardia, DateTime InicioGuardia, DateTime FinGuardia, IReadOnlyList<PacientePanelTurnoDto> Pacientes,
    int TotalVencidas, int TotalSugerencias, string Aviso);

public sealed record ObtenerPanelTurnoConsulta(Guid UsuarioId);

/// <summary>"Mi turno" del personal clínico de un servicio (enfermera o médico). Sin servicio asignado: 403 <c>usuario-sin-servicio</c>.</summary>
public sealed class ObtenerPanelTurno(
    IRepositorioUsuarios usuarios,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioAlertas alertas,
    IRepositorioPlanCuidados plan,
    IRepositorioCatalogoNnn catalogo,
    IRepositorioNotas notas,
    ServicioAgendaPlan agenda,
    CatalogoTurnos turnos,
    IReloj reloj)
{
    public async Task<Resultado<PanelTurnoDto>> EjecutarAsync(ObtenerPanelTurnoConsulta consulta, CancellationToken ct = default)
    {
        if ((await usuarios.ObtenerPorIdAsync(consulta.UsuarioId, ct))?.ServicioId is not { } servicioId)
        {
            return ErroresAlerta.SinServicio;
        }

        var ahora = reloj.AhoraUtc;
        var (turno, guardia) = Guardias.Actual(turnos, ahora);
        var clave = Guardias.Clave(turno);
        var activas = await hospitalizaciones.ListarActivasDeServicioAsync(servicioId, ct);
        var ids = activas.Select(h => h.HospitalizacionId).ToList();
        var ultimas = await evaluaciones.ObtenerUltimasAsync(ids, ct);
        var planes = await plan.ObtenerPlanesActivosAsync(ids, ct);
        var abiertas = (await alertas.ListarAbiertasAsync(servicioId, ct)).ToList();
        var c = await catalogo.ObtenerAsync(ct);

        var pacientes = new List<(PacientePanelTurnoDto Dto, int Nivel)>();
        foreach (var h in activas)
        {
            var lectura = planes.GetValueOrDefault(h.HospitalizacionId) ?? PlanCuidadosLectura.Vacio(h.HospitalizacionId);
            var a = lectura.Intervenciones.Count == 0
                ? new AgendaHospitalizacion(ultimas.TryGetValue(h.HospitalizacionId, out var e0) ? ProtocoloNnn.Nivel(e0.News2()) : NivelRespuestaNews2.Cero,
                    ultimas.GetValueOrDefault(h.HospitalizacionId), new Dictionary<Guid, ItemAgenda>())
                : await agenda.CalcularAsync(lectura, ct);
            var evaluacion = ultimas.GetValueOrDefault(h.HospitalizacionId);
            var alerta = abiertas.Where(x => x.HospitalizacionId == h.HospitalizacionId)
                .OrderByDescending(x => x.Prioridad).ThenBy(x => x.CreadaEn).FirstOrDefault();
            PendientePlanDto Pendiente(IntervencionPlan i, ItemAgenda item) => new(
                i.Id, i.PlanDiagnosticoId, i.IntervencionCodigo, c.Etiqueta(TaxonomiaNnn.Nic, i.IntervencionCodigo), item.ProximaEn, TextosNnn.De(item.Estado));
            var items = lectura.Intervenciones.Where(i => a.Items.ContainsKey(i.Id)).Select(i => (i, item: a.Items[i.Id])).ToList();
            var valoracionesPendientes = lectura.Resultados.Count(r => !lectura.ValoracionesDe(r.Id).Any(v => v.ValoradaEn >= guardia.InicioUtc));

            pacientes.Add((new PacientePanelTurnoDto(
                h.PacienteId, h.HospitalizacionId, h.NombreCompleto, h.CodigoCama, evaluacion?.News2Total,
                evaluacion is null ? null : evaluacion.News2Completo && evaluacion.MewsCompleto, TextosNnn.De(a.Nivel),
                alerta is null ? null : new AlertaTurnoDto(alerta.Id, TextosAlerta.De(alerta.Tipo), TextosAlerta.De(alerta.Prioridad), alerta.CreadaEn),
                items.Where(x => x.item.Estado == EstadoAgenda.Vencida).OrderBy(x => x.item.ProximaEn).Select(x => Pendiente(x.i, x.item)).ToList(),
                items.Where(x => x.item.Estado == EstadoAgenda.Proxima).OrderBy(x => x.item.ProximaEn).Select(x => Pendiente(x.i, x.item)).ToList(),
                (await plan.ListarSugerenciasPendientesAsync(h.HospitalizacionId, ct)).Count,
                valoracionesPendientes,
                clave is not null && await notas.ObtenerDeTurnoAsync(h.HospitalizacionId, clave, ct) is not null), (int)a.Nivel));
        }

        // Por riesgo: nivel de respuesta NEWS2, total, alerta abierta, lo vencido y la cama.
        var ordenados = pacientes
            .OrderByDescending(p => p.Nivel)
            .ThenByDescending(p => p.Dto.News2 ?? -1)
            .ThenByDescending(p => p.Dto.Alerta is not null)
            .ThenByDescending(p => p.Dto.Vencidas.Count)
            .ThenBy(p => p.Dto.Cama, StringComparer.Ordinal)
            .Select(p => p.Dto)
            .ToList();
        return new PanelTurnoDto(servicioId, clave, guardia.InicioUtc, guardia.FinUtc, ordenados,
            ordenados.Sum(p => p.Vencidas.Count), ordenados.Sum(p => p.SugerenciasPendientes), TextosNnn.Aviso);
    }
}

/// <summary>Cuándo se le mandó el último push agrupado de recordatorios a cada enfermera (en memoria: perderlo al reiniciar no daña).</summary>
public sealed class EstadoRecordatoriosPlan
{
    public ConcurrentDictionary<Guid, DateTime> UltimoPush { get; } = new();
}

/// <summary>
/// Lo que vigila el plan de cuidados (lo corre el worker de alertas en cada vuelta). Sin jobs propios ni tablas nuevas: se
/// decide por la ventana entre la revisión anterior y ahora, como el aviso de cambio de turno.
/// <list type="number">
/// <item>Recalcula las sugerencias de cada hospitalización activa: un criterio del wearable que ya persistió aparece sin
/// esperar la próxima evaluación guardada (y las retenidas expiran a su tiempo).</item>
/// <item>Si algo del plan se venció desde la revisión anterior, avisa al servicio por SignalR (<c>recordatoriosPlan</c>,
/// solo cantidades) y, como máximo cada 30 min por enfermera de turno, con un push agrupado ("5 intervenciones en 3
/// pacientes").</item>
/// <item>Interrumpe (push inmediato) solo si una intervención lleva 15 min vencida con NEWS2 de 5 o más.</item>
/// <item>Una hora antes del cambio de turno: valoraciones NOC y notas del turno que faltan.</item>
/// </list>
/// Las sugerencias nunca van por push (solo el contador). Ningún aviso lleva códigos ni etiquetas NANDA-I (Fable; Sendelbach
/// y Funk 2013: entre el 72 y el 99 % de las alarmas no son accionables).
/// </summary>
public sealed class VigilarPlanCuidados(
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioPlanCuidados plan,
    IRepositorioNotas notas,
    ServicioSugerenciasNnn sugerencias,
    ServicioAgendaPlan agenda,
    NotificadorPlanCuidados notificadorPlan,
    INotificadorTiempoReal tiempoReal,
    INotificadorPush push,
    IRepositorioDispositivosMoviles moviles,
    ITurnoVigente turnoVigente,
    IUnidadDeTrabajo unidadDeTrabajo,
    EstadoRecordatoriosPlan estado,
    CatalogoTurnos turnos,
    IBitacoraFallos bitacora,
    IReloj reloj)
{
    public sealed record Resumen(int SugerenciasActualizadas, int PacientesConVencidas, int PushEnviados);

    public static readonly TimeSpan EntrePushAgrupados = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan AvisoCierreGuardia = TimeSpan.FromHours(1);

    public const string MotivoVencidas = "vencidas";

    public const string MotivoCierreGuardia = "cierre-guardia";

    private sealed record Situacion(HospitalizacionActivaLectura Hospitalizacion, AgendaHospitalizacion Agenda, int ValoracionesPendientes,
        bool NotaPendiente, bool NuevaVencida, bool Urgente)
    {
        public RecordatorioPacienteDto Dto() => new(
            Hospitalizacion.PacienteId, Hospitalizacion.HospitalizacionId, Hospitalizacion.CodigoCama, Agenda.Vencidas, Agenda.Proximas,
            ValoracionesPendientes, NotaPendiente);
    }

    public async Task<Resumen> EjecutarAsync(DateTime? ultimaRevision, CancellationToken ct = default)
    {
        var ahora = reloj.AhoraUtc;
        var activas = await hospitalizaciones.ListarActivasDeServicioAsync(null, ct);

        var actualizadas = 0;
        foreach (var h in activas)
        {
            try
            {
                var calculadas = await sugerencias.SincronizarAsync(h.HospitalizacionId, h.PacienteId, ct);
                if (calculadas.Cambio)
                {
                    await notificadorPlan.SugerenciasAsync(h.HospitalizacionId, calculadas.Sugerencias.Count, ct);
                    actualizadas++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                bitacora.Registrar($"Sugerencias NNN de la hospitalización {h.HospitalizacionId} en el vigilante del plan", ex);
            }
        }

        if (ultimaRevision is not { } desde)
        {
            return new Resumen(actualizadas, 0, 0);
        }

        var (turno, guardia) = Guardias.Actual(turnos, ahora);
        var clave = Guardias.Clave(turno);
        var cierre = guardia.FinUtc - AvisoCierreGuardia is var marca && marca > desde && marca <= ahora;
        var planes = await plan.ObtenerPlanesActivosAsync(activas.Select(h => h.HospitalizacionId).ToList(), ct);
        var situaciones = new List<Situacion>();
        foreach (var h in activas)
        {
            if (planes.GetValueOrDefault(h.HospitalizacionId) is not { Intervenciones.Count: > 0 } lectura)
            {
                continue;
            }

            var a = await agenda.CalcularAsync(lectura, ct);
            var vencidas = a.Items.Values.Where(i => i.Estado == EstadoAgenda.Vencida && i.ProximaEn is not null).ToList();
            situaciones.Add(new Situacion(
                h, a,
                lectura.Resultados.Count(r => !lectura.ValoracionesDe(r.Id).Any(v => v.ValoradaEn >= guardia.InicioUtc)),
                cierre && clave is not null && await notas.ObtenerDeTurnoAsync(h.HospitalizacionId, clave, ct) is null,
                vencidas.Any(i => i.ProximaEn > desde && i.ProximaEn <= ahora),
                a.Nivel >= NivelRespuestaNews2.Medio && vencidas.Any(i =>
                    i.ProximaEn + AgendaIntervenciones.ToleranciaUrgente > desde && i.ProximaEn + AgendaIntervenciones.ToleranciaUrgente <= ahora)));
        }

        var enviados = 0;
        foreach (var servicio in situaciones.GroupBy(s => s.Hospitalizacion.ServicioId))
        {
            try
            {
                enviados += await AvisarServicioAsync(servicio.Key, servicio.ToList(), cierre, ahora, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                bitacora.Registrar($"Recordatorios del plan del servicio {servicio.Key}", ex);
            }
        }

        return new Resumen(actualizadas, situaciones.Count(s => s.Agenda.Vencidas > 0), enviados);
    }

    private async Task<int> AvisarServicioAsync(Guid servicioId, IReadOnlyList<Situacion> situaciones, bool cierre, DateTime ahora, CancellationToken ct)
    {
        var enviados = 0;
        var enfermeras = (await turnoVigente.EnfermerasDeTurnoAsync(servicioId, ahora, ct)).Enfermeras;

        if (situaciones.Any(s => s.NuevaVencida))
        {
            await tiempoReal.RecordatoriosPlanAsync(servicioId, new RecordatoriosPlanDto(MotivoVencidas,
                situaciones.Where(s => s.Agenda.Vencidas > 0).Select(s => s.Dto()).ToList()), ct);

            // Un push agrupado por enfermera, como máximo cada 30 min.
            var vencidas = situaciones.Sum(s => s.Agenda.Vencidas);
            var pacientes = situaciones.Count(s => s.Agenda.Vencidas > 0);
            var destinatarios = enfermeras
                .Where(e => !estado.UltimoPush.TryGetValue(e, out var ultimo) || ahora - ultimo >= EntrePushAgrupados)
                .ToList();
            if (destinatarios.Count > 0)
            {
                enviados += await EnviarPushAsync(destinatarios, new MensajePush(
                    "Vitalify · Plan de cuidados",
                    $"{vencidas} {(vencidas == 1 ? "intervención vencida" : "intervenciones vencidas")} en {pacientes} {(pacientes == 1 ? "paciente" : "pacientes")}.",
                    new Dictionary<string, string> { ["tipo"] = "recordatorio-plan", ["motivo"] = MotivoVencidas }), ct);
                destinatarios.ForEach(e => estado.UltimoPush[e] = ahora);
            }
        }

        // Interrumpe solo lo urgente: vencida hace 15 min con NEWS2 de 5 o más.
        foreach (var s in situaciones.Where(s => s.Urgente))
        {
            enviados += await EnviarPushAsync(enfermeras, new MensajePush(
                TextosPush.Titulo(s.Hospitalizacion.CodigoCama),
                "Intervención del plan vencida hace 15 min con NEWS2 de 5 o más.",
                new Dictionary<string, string>
                {
                    ["tipo"] = "recordatorio-plan",
                    ["motivo"] = "urgente",
                    ["pacienteId"] = s.Hospitalizacion.PacienteId.ToString(),
                }), ct);
        }

        if (cierre)
        {
            var valoraciones = situaciones.Sum(s => s.ValoracionesPendientes);
            var notasPendientes = situaciones.Count(s => s.NotaPendiente);
            if (valoraciones + notasPendientes > 0)
            {
                await tiempoReal.RecordatoriosPlanAsync(servicioId, new RecordatoriosPlanDto(MotivoCierreGuardia,
                    situaciones.Where(s => s.ValoracionesPendientes > 0 || s.NotaPendiente).Select(s => s.Dto()).ToList()), ct);
                enviados += await EnviarPushAsync(enfermeras, new MensajePush(
                    "Vitalify · Cambio de turno en 1 h",
                    $"Faltan {valoraciones} {(valoraciones == 1 ? "valoración NOC" : "valoraciones NOC")} y {notasPendientes} {(notasPendientes == 1 ? "nota del turno" : "notas del turno")}.",
                    new Dictionary<string, string> { ["tipo"] = "recordatorio-plan", ["motivo"] = MotivoCierreGuardia }), ct);
            }
        }

        return enviados;
    }

    private async Task<int> EnviarPushAsync(IReadOnlyCollection<Guid> usuarios, MensajePush mensaje, CancellationToken ct)
    {
        var dispositivos = usuarios.Count == 0 ? [] : await moviles.ListarDeUsuariosAsync(usuarios, ct);
        if (dispositivos.Count == 0)
        {
            return 0;
        }

        var resultados = await push.EnviarAsync(dispositivos.Select(d => d.Token).ToList(), mensaje, ct);
        var borrar = dispositivos.Where((_, i) => i < resultados.Count && resultados[i] == ResultadoEnvioPush.NoRegistrado).ToList();
        if (borrar.Count > 0)
        {
            borrar.ForEach(moviles.Eliminar);
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }

        return resultados.Count(r => r == ResultadoEnvioPush.Enviado);
    }
}
