using System.Globalization;
using FluentValidation;
using Vitalify.Application.Alertas;
using Vitalify.Application.Comun;
using Vitalify.Application.Historial;
using Vitalify.Application.Notas;
using Vitalify.Application.Puertos;
using Vitalify.Application.Turnos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Nnn;
using Vitalify.Domain.Notas;
using Vitalify.Domain.Telemetria;
using Vitalify.Domain.Turnos;

namespace Vitalify.Application.Nnn;

/// <param name="NotaFirmadaId">Si ya se firmó la nota de esta guardia, su id (se corrige en vez de firmar otra).</param>
public sealed record BorradorNotaTurnoDto(Guid PacienteId, Guid HospitalizacionId, string? Guardia, string Texto, Guid? NotaFirmadaId, string Aviso);

public sealed record ObtenerBorradorNotaTurnoConsulta(Guid PacienteId, Guid UsuarioId, string? Ip);

/// <summary>
/// Arma el borrador de la nota de enfermería del turno en los cinco pasos del PAE con los datos de la guardia actual
/// (<see cref="RedactorNotaPae"/>): NEWS2 y MEWS al inicio, el peor y ahora; extremos del sensor y de la toma manual por
/// separado; tomas, oxígeno, ACVPU y dolor; alertas; diagnósticos; NIC con su frecuencia; ejecuciones; valoraciones NOC
/// contra basal y meta, y lo que queda para el turno siguiente. Leerlo cuenta como consulta del historial.
/// </summary>
public sealed class ObtenerBorradorNotaTurno(
    ContextoPlanCuidados contexto,
    ContextoHospitalizaciones contextos,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioLecturas lecturas,
    IRepositorioObservaciones observaciones,
    IRepositorioAlertas alertas,
    IRepositorioPlanCuidados plan,
    IRepositorioCatalogoNnn catalogo,
    IRepositorioNotas notas,
    ServicioAgendaPlan agenda,
    DirectorioProfesionales directorio,
    AuditoriaHistorial auditoria,
    CatalogoTurnos turnos,
    IReloj reloj)
{
    public async Task<Resultado<BorradorNotaTurnoDto>> EjecutarAsync(ObtenerBorradorNotaTurnoConsulta consulta, CancellationToken ct = default)
    {
        var hospitalizacion = await contexto.HospitalizacionActivaAsync(consulta.PacienteId, ct);
        if (!hospitalizacion.EsExito)
        {
            return hospitalizacion.Error;
        }

        var h = hospitalizacion.Valor;
        var ahora = reloj.AhoraUtc;
        var (turno, guardia) = Guardias.Actual(turnos, ahora);
        var clave = Guardias.Clave(turno);
        var desde = guardia.InicioUtc;
        var cama = (await contextos.ObtenerAsync(h.Id, ct))?.CodigoCama ?? "—";
        var evals = await evaluaciones.ListarAsync(h.Id, desde, ahora, ct);
        var sensor = await lecturas.ListarPorHospitalizacionAsync(h.Id, desde, ahora, ct);
        var tomas = (await observaciones.ListarAsync(h.Id, desde, ahora, ct)).OrderBy(t => t.ObservadaEn).ToList();
        var deAlertas = (await alertas.ListarDeHospitalizacionAsync(h.Id, ct)).Select(a => a.Alerta)
            .Where(a => a.CreadaEn >= desde || a.EstaAbierta).OrderBy(a => a.CreadaEn).ToList();
        var lectura = await plan.ObtenerPlanAsync(h.Id, ct);
        var c = await catalogo.ObtenerAsync(ct);
        var a = await agenda.CalcularAsync(lectura, ct);
        var personas = await directorio.ObtenerAsync(
            lectura.Ejecuciones.Select(e => e.EjecutadaPor).Concat(lectura.Diagnosticos.Select(d => d.ConfirmadoPor).OfType<Guid>()), ct);

        var secciones = new SeccionesNotaPae(
            Valoracion(evals, sensor, tomas, deAlertas),
            Diagnostico(lectura, c, personas, desde),
            Planificacion(lectura, c, a),
            Ejecucion(lectura, c, personas, a, desde),
            Evaluacion(lectura, c, a, desde));
        var etiqueta = turno?.Tipo == TipoTurno.GuardiaNocturna ? "Guardia nocturna" : "Guardia diurna";
        var encabezado = $"Nota de enfermería del turno (PAE) · Cama {cama} · {etiqueta} del {guardia.InicioUtc.AddHours(-5):dd/MM/yyyy} " +
                         $"({RedactorNotaPae.Hora(guardia.InicioUtc)}–{RedactorNotaPae.Hora(guardia.FinUtc)})";
        var texto = RedactorNotaPae.Redactar(encabezado, secciones);

        var firmada = clave is null ? null : await notas.ObtenerDeTurnoAsync(h.Id, clave, ct);
        var auditado = await auditoria.RegistrarAsync(consulta.PacienteId, h.Id, "nota-turno-borrador", consulta.UsuarioId, consulta.Ip, ct);
        return auditado.EsExito
            ? new BorradorNotaTurnoDto(consulta.PacienteId, h.Id, clave, texto, firmada?.Id, TextosNnn.Aviso)
            : auditado.Error;
    }

    private static List<string> Valoracion(
        IReadOnlyList<EvaluacionRiesgo> evals, IReadOnlyList<LecturaSignos> sensor, IReadOnlyList<ObservacionEnfermeria> tomas, IReadOnlyList<AlertaClinica> deAlertas)
    {
        var lineas = new List<string>();
        if (evals.Count > 0)
        {
            var fin = evals[^1];
            var parcial = fin.News2Completo ? string.Empty : $" (parcial: al menos {fin.News2Total})";
            lineas.Add($"NEWS2: {evals[0].News2Total} al inicio, peor {evals.Max(e => e.News2Total)}, ahora {fin.News2Total}{parcial}. " +
                       $"MEWS: {evals[0].MewsTotal} al inicio, peor {evals.Max(e => e.MewsTotal)}, ahora {fin.MewsTotal}.");
        }
        else
        {
            lineas.Add("Sin evaluaciones NEWS2/MEWS en el turno.");
        }

        static string? Rango(string nombre, IEnumerable<decimal?> valores, string unidad, int decimales = 0)
        {
            var lista = valores.OfType<decimal>().ToList();
            return lista.Count == 0
                ? null
                : $"{nombre} {RedactorNotaPae.Numero(lista.Min(), decimales)}–{RedactorNotaPae.Numero(lista.Max(), decimales)} {unidad}";
        }

        var deSensor = new[]
        {
            Rango("FC", sensor.Select(l => (decimal?)l.Fc), "lpm"),
            Rango("SpO2", sensor.Select(l => (decimal?)l.Spo2), "%"),
            Rango("FR", sensor.Select(l => (decimal?)l.Fr), "rpm"),
            Rango("temperatura de piel", sensor.Select(l => l.Temperatura), "°C", 1),
        }.OfType<string>().ToList();
        if (deSensor.Count > 0)
        {
            lineas.Add($"Wearable (extremos): {string.Join("; ", deSensor)}.");
        }

        var manuales = new[]
        {
            Rango("PAS", tomas.Select(t => (decimal?)t.Pas), "mmHg"),
            Rango("FR", tomas.Select(t => (decimal?)t.Fr), "rpm"),
            Rango("temperatura (termómetro)", tomas.Select(t => t.Temperatura), "°C", 1),
        }.OfType<string>().ToList();
        if (manuales.Count > 0)
        {
            lineas.Add($"Toma manual (extremos): {string.Join("; ", manuales)}.");
        }

        if (tomas.Count > 0)
        {
            var ultima = tomas[^1];
            lineas.Add($"Tomas manuales: {tomas.Count} (última a las {RedactorNotaPae.Hora(ultima.ObservadaEn)}).");
            var oxigeno = ultima.OxigenoSuplementario switch
            {
                true => $"sí{(ultima.DispositivoOxigeno is { } d ? $", {Dispositivo(d)}" : string.Empty)}{(ultima.FlujoOxigenoLpm is { } f ? $" {RedactorNotaPae.Numero(f)} L/min" : string.Empty)}",
                false => "no",
                _ => null,
            };
            var datos = new[]
            {
                oxigeno is null ? null : $"oxígeno: {oxigeno}",
                ultima.Conciencia is { } n ? $"ACVPU: {Letra(n)}" : null,
                ultima.Contexto?.Dolor is { } dolor ? $"dolor: {dolor}/10" : null,
            }.OfType<string>().ToList();
            if (datos.Count > 0)
            {
                lineas.Add($"Última toma: {string.Join("; ", datos)}.");
            }
        }
        else
        {
            lineas.Add("Sin tomas manuales en el turno.");
        }

        foreach (var alerta in deAlertas)
        {
            var atencion = alerta.AtendidaEn is { } at ? $", atendida a las {RedactorNotaPae.Hora(at)}"
                : alerta.EstaAbierta ? ", sigue abierta" : ", resuelta";
            lineas.Add($"Alerta {TextosAlerta.De(alerta.Tipo)} ({TextosAlerta.De(alerta.Prioridad)}) a las {RedactorNotaPae.Hora(alerta.CreadaEn)}{atencion}.");
        }

        return lineas;
    }

    private static List<string> Diagnostico(
        PlanCuidadosLectura lectura, CatalogoNnnLectura c, IReadOnlyDictionary<Guid, ProfesionalDto> personas, DateTime desde)
    {
        var lineas = lectura.Diagnosticos.Where(d => d.EstaActivo).Select(d =>
            $"{d.DiagnosticoCodigo} {c.Etiqueta(TaxonomiaNnn.Nanda, d.DiagnosticoCodigo)} (activo desde el {d.ConfirmadoEn?.AddHours(-5):dd/MM HH:mm}; " +
            $"{(d.Origen == OrigenDiagnostico.Sugerido ? $"sugerido por {d.ReglaOrigen}" : "elegido del catálogo")}, confirmó " +
            $"{(d.ConfirmadoPor is { } p ? DirectorioProfesionales.De(personas, p).Nombre : "—")}).").ToList();
        lineas.AddRange(lectura.Diagnosticos
            .Where(d => !d.EstaActivo && d.CerradoEn >= desde)
            .Select(d => $"{d.DiagnosticoCodigo} {c.Etiqueta(TaxonomiaNnn.Nanda, d.DiagnosticoCodigo)}: " +
                         $"{(d.Estado == EstadoDiagnosticoPlan.Resuelto ? "resuelto" : "descartado")} a las {RedactorNotaPae.Hora(d.CerradoEn!.Value)} ({d.MotivoCierre})."));
        return lineas;
    }

    private static List<string> Planificacion(PlanCuidadosLectura lectura, CatalogoNnnLectura c, AgendaHospitalizacion a)
    {
        var lineas = new List<string>();
        foreach (var d in lectura.Diagnosticos.Where(d => d.EstaActivo))
        {
            lineas.AddRange(lectura.IntervencionesDe(d.Id).Where(i => i.Activa).Select(i =>
                $"{d.DiagnosticoCodigo} · NIC {i.IntervencionCodigo} {c.Etiqueta(TaxonomiaNnn.Nic, i.IntervencionCodigo)}: {Frecuencia(i, a.Nivel)}."));
            lineas.AddRange(lectura.ResultadosDe(d.Id).Select(r =>
                $"{d.DiagnosticoCodigo} · NOC {r.ResultadoCodigo} {c.Etiqueta(TaxonomiaNnn.Noc, r.ResultadoCodigo)}: basal {r.PuntuacionBasal}, meta {r.PuntuacionMeta}."));
        }

        return lineas;
    }

    private static List<string> Ejecucion(
        PlanCuidadosLectura lectura, CatalogoNnnLectura c, IReadOnlyDictionary<Guid, ProfesionalDto> personas, AgendaHospitalizacion a, DateTime desde)
    {
        var codigoDe = lectura.Intervenciones.ToDictionary(i => i.Id, i => i.IntervencionCodigo);
        var lineas = lectura.Ejecuciones.Where(e => e.EjecutadaEn >= desde).OrderBy(e => e.EjecutadaEn).Select(e =>
        {
            var codigo = codigoDe.GetValueOrDefault(e.PlanIntervencionId) ?? "—";
            var origen = e.Origen switch
            {
                OrigenEjecucion.TomaManual => " (con la toma manual)",
                OrigenEjecucion.Alerta => " (al atender una alerta)",
                _ => string.Empty,
            };
            var aviso = codigo == ReglasNnn.NicColaboracionMedica ? " · aviso al médico" : string.Empty;
            return $"{RedactorNotaPae.Hora(e.EjecutadaEn)} NIC {codigo} {c.Etiqueta(TaxonomiaNnn.Nic, codigo)}{aviso} — " +
                   $"{DirectorioProfesionales.De(personas, e.EjecutadaPor).Nombre}{origen}{(e.Observacion is { } o ? $": {o}" : string.Empty)}.";
        }).ToList();
        lineas.Add($"Al cierre: {a.Vencidas} {(a.Vencidas == 1 ? "intervención vencida" : "intervenciones vencidas")} y {a.Proximas} próximas.");
        return lineas;
    }

    private static List<string> Evaluacion(PlanCuidadosLectura lectura, CatalogoNnnLectura c, AgendaHospitalizacion a, DateTime desde)
    {
        var lineas = new List<string>();
        foreach (var d in lectura.Diagnosticos.Where(d => d.EstaActivo))
        {
            foreach (var r in lectura.ResultadosDe(d.Id))
            {
                var enTurno = lectura.ValoracionesDe(r.Id).Where(v => v.ValoradaEn >= desde).ToList();
                var nombre = $"NOC {r.ResultadoCodigo} {c.Etiqueta(TaxonomiaNnn.Noc, r.ResultadoCodigo)}";
                if (PresentadorPlanCuidados.Ultima(enTurno) is { } u)
                {
                    var estado = u.Puntuacion >= r.PuntuacionMeta ? "meta alcanzada" : u.Puntuacion > r.PuntuacionBasal ? "mejora" : "sin mejora";
                    lineas.Add($"{nombre}: {RedactorNotaPae.Numero((decimal)u.Puntuacion)} a las {RedactorNotaPae.Hora(u.En)} " +
                               $"(basal {r.PuntuacionBasal}, meta {r.PuntuacionMeta}: {estado}).");
                }
                else
                {
                    lineas.Add($"{nombre}: sin valorar en el turno.");
                }
            }
        }

        var siguientes = lectura.Intervenciones.Where(i => a.Items.TryGetValue(i.Id, out var item) && item.Estado == EstadoAgenda.Vencida)
            .Select(i => i.IntervencionCodigo).Distinct(StringComparer.Ordinal).ToList();
        if (siguientes.Count > 0)
        {
            lineas.Add($"Para el turno siguiente: NIC {string.Join(", ", siguientes)} pendientes.");
        }

        return lineas;
    }

    private static string Frecuencia(IntervencionPlan i, NivelRespuestaNews2 nivel) => i.Frecuencia switch
    {
        FrecuenciaIntervencion.UnaVez => "una vez",
        FrecuenciaIntervencion.CadaTurno => "cada turno",
        FrecuenciaIntervencion.CadaHora => "cada hora",
        FrecuenciaIntervencion.SegunNecesidad => "según necesidad",
        _ => ProtocoloNnn.PautaDe(i.IntervencionCodigo, nivel) switch
        {
            { SegunNecesidad: true } => "por protocolo NEWS2 (ahora según necesidad)",
            { PorTurno: true } => "por protocolo NEWS2 (ahora cada turno)",
            { Intervalo: { } t } => $"por protocolo NEWS2 (ahora cada {(t.TotalHours >= 1 ? $"{t.TotalHours:0} h" : $"{t.TotalMinutes:0} min")})",
            _ => "por protocolo NEWS2",
        },
    };

    private static string Dispositivo(DispositivoOxigeno d) => d switch
    {
        DispositivoOxigeno.CanulaNasal => "cánula nasal",
        DispositivoOxigeno.MascarillaSimple => "mascarilla simple",
        DispositivoOxigeno.Venturi => "Venturi",
        DispositivoOxigeno.MascarillaReservorio => "mascarilla con reservorio",
        _ => "alto flujo",
    };

    private static string Letra(NivelConciencia nivel) => nivel switch
    {
        NivelConciencia.Alerta => "A",
        NivelConciencia.ConfusionNueva => "C",
        NivelConciencia.RespondeVoz => "V",
        NivelConciencia.RespondeDolor => "P",
        _ => "U",
    };
}

public sealed record FirmarNotaTurnoComando(Guid PacienteId, string? Texto, Guid UsuarioId, string? Ip);

public sealed class FirmarNotaTurnoValidador : AbstractValidator<FirmarNotaTurnoComando>
{
    public FirmarNotaTurnoValidador() =>
        RuleFor(c => c.Texto).Must(t => t?.Trim().Length is >= NotaClinica.LargoMinimoTexto and <= NotaClinica.LargoMaximoTexto)
            .WithMessage($"El texto es obligatorio y debe tener entre {NotaClinica.LargoMinimoTexto} y {NotaClinica.LargoMaximoTexto} caracteres.");
}

/// <summary>
/// La enfermera firma la nota del PAE de la guardia actual (el borrador revisado y, si hizo falta, corregido): queda como
/// <see cref="NotaClinica"/> de tipo <c>ProcesoEnfermeria</c>, una por hospitalización y guardia (409
/// <c>nota-turno-ya-registrada</c>; se corrige 24 h con la edición de notas). Se audita como <c>NotaRegistrada</c>, sin el
/// texto. Sin firma digital (Ley 27269) no reemplaza el registro oficial: es una limitación declarada.
/// </summary>
public sealed class FirmarNotaTurno(
    IValidator<FirmarNotaTurnoComando> validador,
    ContextoPlanCuidados contexto,
    IRepositorioNotas notas,
    IRepositorioAuditoria auditoria,
    IUnidadDeTrabajo unidadDeTrabajo,
    PresentadorNotas presentador,
    CatalogoTurnos turnos,
    IReloj reloj)
{
    public async Task<Resultado<NotaDto>> EjecutarAsync(FirmarNotaTurnoComando comando, CancellationToken ct = default)
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
        var ahora = reloj.AhoraUtc;
        if (Guardias.Clave(Guardias.Actual(turnos, ahora).Turno) is not { } guardia)
        {
            return ErroresComunes.ValidacionDeCampo("texto", "No hay una guardia definida para este momento.");
        }

        if (await notas.ObtenerDeTurnoAsync(h.Id, guardia, ct) is not null)
        {
            return ErroresNnn.NotaTurnoYaRegistrada;
        }

        NotaClinica nota;
        try
        {
            nota = NotaClinica.RegistrarDeTurno(h.Id, h.PacienteId, comando.UsuarioId, guardia, comando.Texto!, ahora);
        }
        catch (ExcepcionDeDominio ex)
        {
            return ErroresComunes.ValidacionDeCampo("texto", ex.Message);
        }

        notas.Agregar(nota);
        auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.NotaRegistrada, ahora, comando.UsuarioId, comando.Ip,
            $"Nota {nota.Id} del PAE de la hospitalización {h.Id} ({guardia.ToString(CultureInfo.InvariantCulture)})."));
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (!guardado.EsExito)
        {
            return guardado.Error;
        }

        return (await presentador.PresentarAsync([nota], ct))[0];
    }
}
