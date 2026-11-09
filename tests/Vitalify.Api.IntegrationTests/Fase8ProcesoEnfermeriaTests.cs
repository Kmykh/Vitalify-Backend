using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Alertas;
using Vitalify.Application.Nnn;
using Vitalify.Application.Pacientes;
using Vitalify.Domain.Nnn;

namespace Vitalify.Api.IntegrationTests;

/// <summary>
/// Fase 8: proceso de enfermería NNN. El motor sugiere, la enfermera confirma o descarta, registra lo que hizo y valora con
/// NOC; el médico lee. Ningún log, auditoría ni evento de SignalR lleva un código o una etiqueta NANDA-I.
/// </summary>
public sealed class Fase8ProcesoEnfermeriaTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    private static int _seq = 800_000;

    private static readonly object ConfirmarE1 = new
    {
        diagnosticoCodigo = "00032",
        origen = "Sugerido",
        reglaOrigen = "R1",
        intervenciones = new[]
        {
            new { codigo = "3350", frecuencia = "CadaHora" },
            new { codigo = "3140", frecuencia = "CadaTurno" },
            new { codigo = "7710", frecuencia = "UnaVez" },
        },
        resultados = new[]
        {
            new { codigo = "0415", puntuacionBasal = 2, puntuacionMeta = 4 },
            new { codigo = "0802", puntuacionBasal = 2, puntuacionMeta = 4 },
        },
    };

    /// <summary>E1 desde el wearable: FR 26 (3 puntos) y SpO2 90 (3 puntos), en tiempo real.</summary>
    private async Task<(HttpClient Enfermera, FichaPacienteDto Ficha)> PacienteE1Async()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        await (await ClienteAdministradorAsync()).RegistrarTelemetriaAsync(new
        {
            dispositivo = sensor.Codigo, ts = ClienteApi.Ts(Fabrica.Reloj.AhoraUtc), seq = Interlocked.Increment(ref _seq), fc = 80, fr = 26, spo2 = 90,
            caida = false,
        });
        return (enfermera, ficha);
    }

    private static Task<HttpResponseMessage> ConfirmarAsync(HttpClient cliente, Guid pacienteId, object cuerpo) =>
        cliente.PostAsJsonAsync($"/api/v1/pacientes/{pacienteId}/plan-cuidados/diagnosticos", cuerpo);

    private static async Task<DiagnosticoPlanDto> ConfirmadoAsync(HttpClient enfermera, Guid pacienteId, object? cuerpo = null)
    {
        var respuesta = await ConfirmarAsync(enfermera, pacienteId, cuerpo ?? ConfirmarE1);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<DiagnosticoPlanDto>())!;
    }

    private static async Task<SugerenciasPacienteDto> SugerenciasAsync(HttpClient cliente, Guid pacienteId) =>
        (await cliente.GetFromJsonAsync<SugerenciasPacienteDto>($"/api/v1/pacientes/{pacienteId}/nnn/sugerencias"))!;

    [Fact]
    public async Task F8_E1_el_motor_sugiere_y_la_enfermera_confirma_ejecuta_y_valora_con_la_puntuacion_sugerida()
    {
        var (enfermera, ficha) = await PacienteE1Async();

        var sugerencias = await SugerenciasAsync(enfermera, ficha.Id);

        Assert.Equal("2026.11.1", sugerencias.VersionReglas);
        Assert.Equal("Vitalify sugiere; la enfermera decide.", sugerencias.Aviso);
        Assert.Equal(["00030", "00032"], sugerencias.Sugerencias.Select(s => s.Diagnostico.Codigo).Order());
        var patron = sugerencias.Sugerencias.Single(s => s.Diagnostico.Codigo == "00032");
        Assert.Equal("Patrón respiratorio ineficaz", patron.Diagnostico.Etiqueta);
        Assert.False(patron.Diagnostico.Verificado);
        Assert.Equal(["3350", "3140", "7710"], patron.Intervenciones.Select(i => i.Codigo));
        Assert.StartsWith("FR 26 rpm: 3 puntos NEWS2", patron.Motivo);
        Assert.Equal("alta", patron.Prioridad);
        Assert.NotNull(patron.AlertaId); // la alerta de riesgo que abrió la misma evaluación
        Assert.Contains(Fabrica.TiempoReal.De("sugerenciasNnn"), e => e.Json.Contains(ficha.Id.ToString(), StringComparison.Ordinal));

        var diagnostico = await ConfirmadoAsync(enfermera, ficha.Id);

        Assert.Equal(("Activo", "Sugerido", "R1"), (diagnostico.Estado, diagnostico.Origen, diagnostico.ReglaOrigen));
        Assert.Equal(patron.AlertaId, diagnostico.AlertaId);
        Assert.Equal(["3140", "3350", "7710"], diagnostico.Intervenciones.Select(i => i.Codigo)); // por fecha y código
        Assert.Equal(["0415", "0802"], diagnostico.Resultados.Select(r => r.Codigo));
        Assert.Equal(["00030"], (await SugerenciasAsync(enfermera, ficha.Id)).Sugerencias.Select(s => s.Diagnostico.Codigo));

        // Ejecución de 3350 y valoración NOC de 0802 con la propuesta del motor (FR en 3 puntos → 1).
        var monitorizacion = diagnostico.Intervenciones.Single(i => i.Codigo == "3350");
        var ejecucion = await enfermera.PostAsJsonAsync(
            $"/api/v1/plan-cuidados/intervenciones/{monitorizacion.Id}/ejecuciones", new { observacion = "FR contada en 1 minuto." });
        Assert.Equal(HttpStatusCode.Created, ejecucion.StatusCode);

        var signosVitales = diagnostico.Resultados.Single(r => r.Codigo == "0802");
        var sugerida = (await enfermera.GetFromJsonAsync<ValoracionSugeridaDto>($"/api/v1/plan-cuidados/resultados/{signosVitales.Id}/valoracion-sugerida"))!;
        Assert.Equal(1, sugerida.Indicadores.Single(i => i.Codigo == "080204").PuntuacionSugerida);
        Assert.Null(sugerida.Indicadores.Single(i => i.Codigo == "080205").PuntuacionSugerida); // PAS: el wearable no la mide

        var valoracion = await enfermera.PostAsJsonAsync($"/api/v1/plan-cuidados/resultados/{signosVitales.Id}/valoraciones",
            new { indicadores = new[] { new { codigo = "080204", puntuacion = 2 }, new { codigo = "0802", puntuacion = 2 } } });
        Assert.Equal(HttpStatusCode.Created, valoracion.StatusCode);
        var registrada = (await valoracion.Content.ReadFromJsonAsync<ValoracionRegistradaDto>())!;
        Assert.Equal(1, registrada.Indicadores.Single(i => i.IndicadorCodigo == "080204").PuntuacionSugerida);
        Assert.Null(registrada.Indicadores.Single(i => i.IndicadorCodigo is null).PuntuacionSugerida);

        var plan = (await enfermera.GetFromJsonAsync<PlanCuidadosDto>($"/api/v1/pacientes/{ficha.Id}/plan-cuidados"))!;
        var enPlan = Assert.Single(plan.Diagnosticos, d => d.Estado == "Activo");
        Assert.Single(enPlan.Intervenciones.Single(i => i.Codigo == "3350").Ejecuciones);
        var serie = enPlan.Resultados.Single(r => r.Codigo == "0802");
        Assert.Equal(2, serie.Valoraciones.Count);
        Assert.Equal(2, serie.UltimaPuntuacion);
        Assert.Contains(Fabrica.TiempoReal.De("planActualizado"), e => e.Json.Contains(ficha.Id.ToString(), StringComparison.Ordinal));

        var registro = await Fabrica.ConsultarAsync(db => db.SugerenciasEmitidas.Where(s => s.PacienteId == ficha.Id).ToListAsync());
        Assert.Equal(EstadoSugerencia.Confirmada, registro.Single(s => s.DiagnosticoCodigo == "00032").Estado);
        Assert.Equal(EstadoSugerencia.Pendiente, registro.Single(s => s.DiagnosticoCodigo == "00030").Estado);
    }

    [Fact]
    public async Task F8_confirmar_el_mismo_diagnostico_dos_veces_da_409()
    {
        var (enfermera, ficha) = await PacienteE1Async();
        await ConfirmadoAsync(enfermera, ficha.Id);

        var otra = await ConfirmarAsync(enfermera, ficha.Id, ConfirmarE1);

        Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
        Assert.Equal("diagnostico-ya-activo", (await otra.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task F8_el_medico_no_confirma_diagnosticos_NANDA_pero_puede_agregar_y_registrar_la_NIC_7710()
    {
        var (enfermera, ficha) = await PacienteE1Async();
        var medico = await ClienteMedicoAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await ConfirmarAsync(medico, ficha.Id, ConfirmarE1)).StatusCode);

        var diagnostico = await ConfirmadoAsync(enfermera, ficha.Id, new
        {
            diagnosticoCodigo = "00030", origen = "Sugerido", reglaOrigen = "R2",
            intervenciones = new[] { new { codigo = "3320", frecuencia = "CadaHora" } },
            resultados = new[] { new { codigo = "0402", puntuacionBasal = 2, puntuacionMeta = 4 } },
        });

        var otraNic = await medico.PostAsJsonAsync($"/api/v1/plan-cuidados/diagnosticos/{diagnostico.Id}/intervenciones", new { codigo = "3350", frecuencia = "CadaHora" });
        Assert.Equal(HttpStatusCode.Forbidden, otraNic.StatusCode);
        Assert.Equal("nnn-solo-enfermeria", (await otraNic.LeerProblemaAsync()).Type);

        var colaboracion = await medico.PostAsJsonAsync($"/api/v1/plan-cuidados/diagnosticos/{diagnostico.Id}/intervenciones", new { codigo = "7710", frecuencia = "UnaVez" });
        Assert.Equal(HttpStatusCode.Created, colaboracion.StatusCode);
        var nic7710 = (await colaboracion.Content.ReadFromJsonAsync<IntervencionPlanDto>())!;
        Assert.Equal(HttpStatusCode.Created, (await medico.PostAsJsonAsync($"/api/v1/plan-cuidados/intervenciones/{nic7710.Id}/ejecuciones", new { })).StatusCode);

        var oxigeno = diagnostico.Intervenciones.Single(i => i.Codigo == "3320");
        Assert.Equal(HttpStatusCode.Forbidden, (await medico.PostAsJsonAsync($"/api/v1/plan-cuidados/intervenciones/{oxigeno.Id}/ejecuciones", new { })).StatusCode);

        // El médico lee el plan (y queda auditado como consulta del historial).
        var plan = (await medico.GetFromJsonAsync<PlanCuidadosDto>($"/api/v1/pacientes/{ficha.Id}/plan-cuidados"))!;
        Assert.Contains(plan.Diagnosticos, d => d.Codigo == "00030" && d.Intervenciones.Any(i => i.Codigo == "7710" && !i.Activa));
    }

    [Fact]
    public async Task F8_descartar_una_sugerencia_exige_motivo_y_queda_registrado()
    {
        var (enfermera, ficha) = await PacienteE1Async();
        var ruta = $"/api/v1/pacientes/{ficha.Id}/plan-cuidados/sugerencias/descartar";

        var sinMotivo = await enfermera.PostAsJsonAsync(ruta, new { diagnosticoCodigo = "00030", reglaOrigen = "R2" });
        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);
        Assert.Contains("motivo", (await sinMotivo.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);

        var descartada = await enfermera.PostAsJsonAsync(ruta, new { diagnosticoCodigo = "00030", reglaOrigen = "R2", motivo = "Sensor mal colocado: SpO2 98 % con oxímetro de pulso." });
        Assert.Equal(HttpStatusCode.Created, descartada.StatusCode);
        var d = (await descartada.Content.ReadFromJsonAsync<DiagnosticoPlanDto>())!;
        Assert.Equal(("Descartado", "R2"), (d.Estado, d.ReglaOrigen));
        Assert.Null(d.ConfirmadoPor);

        // En esta guardia ya no se vuelve a sugerir, y la sugerencia cuenta como descartada.
        Assert.DoesNotContain((await SugerenciasAsync(enfermera, ficha.Id)).Sugerencias, s => s.Diagnostico.Codigo == "00030");
        var registro = await Fabrica.ConsultarAsync(db => db.SugerenciasEmitidas.SingleAsync(s => s.PacienteId == ficha.Id && s.DiagnosticoCodigo == "00030"));
        Assert.Equal(EstadoSugerencia.Descartada, registro.Estado);
    }

    [Fact]
    public async Task F8_un_diagnostico_se_resuelve_con_motivo_y_no_vuelve_a_activo()
    {
        var (enfermera, ficha) = await PacienteE1Async();
        var d = await ConfirmadoAsync(enfermera, ficha.Id);
        var ruta = $"/api/v1/plan-cuidados/diagnosticos/{d.Id}/estado";

        Assert.Equal(HttpStatusCode.BadRequest, (await enfermera.PutAsJsonAsync(ruta, new { estado = "Activo", motivo = "Reaparece el cuadro" })).StatusCode);

        var resuelto = await enfermera.PutAsJsonAsync(ruta, new { estado = "Resuelto", motivo = "FR 18 rpm sostenida por 6 h." });
        Assert.Equal(HttpStatusCode.OK, resuelto.StatusCode);
        Assert.Equal("Resuelto", (await resuelto.Content.ReadFromJsonAsync<DiagnosticoPlanDto>())!.Estado);

        var otraVez = await enfermera.PutAsJsonAsync(ruta, new { estado = "Descartado", motivo = "Se cierra otra vez" });
        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        Assert.Equal("diagnostico-cerrado", (await otraVez.LeerProblemaAsync()).Type);
    }

    [Fact]
    public async Task F8_validaciones_del_plan_meta_menor_que_la_basal_y_codigos_fuera_del_catalogo()
    {
        var (enfermera, ficha) = await PacienteE1Async();

        var metaBaja = await ConfirmarAsync(enfermera, ficha.Id, new
        {
            diagnosticoCodigo = "00146", origen = "Manual",
            intervenciones = new[] { new { codigo = "5820", frecuencia = "CadaTurno" } },
            resultados = new[] { new { codigo = "1211", puntuacionBasal = 4, puntuacionMeta = 2 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, metaBaja.StatusCode);
        Assert.Contains("resultados[0].puntuacionMeta", (await metaBaja.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);

        var fueraDelCatalogo = await ConfirmarAsync(enfermera, ficha.Id, new
        {
            diagnosticoCodigo = "00146", origen = "Manual",
            intervenciones = new[] { new { codigo = "9999", frecuencia = "CadaTurno" } },
            resultados = new[] { new { codigo = "1211", puntuacionBasal = 2, puntuacionMeta = 4 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, fueraDelCatalogo.StatusCode);
        Assert.Contains("intervenciones[0].codigo", (await fueraDelCatalogo.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);

        var manual = await ConfirmadoAsync(enfermera, ficha.Id, new
        {
            diagnosticoCodigo = "00146", origen = "Manual",
            intervenciones = new[] { new { codigo = "5820", frecuencia = "SegunNecesidad" } },
            resultados = new[] { new { codigo = "1211", puntuacionBasal = 2, puntuacionMeta = 4 } },
        });
        Assert.Equal(("Manual", (string?)null), (manual.Origen, manual.ReglaOrigen));
    }

    [Fact]
    public async Task F8_atender_una_alerta_con_intervenciones_ejecutadas_las_registra_con_esa_alerta()
    {
        var (enfermera, ficha) = await PacienteE1Async();
        var d = await ConfirmadoAsync(enfermera, ficha.Id);
        var alerta = (await enfermera.GetFromJsonAsync<List<AlertaDto>>($"/api/v1/pacientes/{ficha.Id}/alertas"))!.Single(a => a.Tipo == "riesgo");

        // Las sugerencias de la evaluación que abrió la alerta: 00032 ya está activo, queda 00030.
        var deLaAlerta = (await enfermera.GetFromJsonAsync<SugerenciasAlertaDto>($"/api/v1/alertas/{alerta.Id}/sugerencias-nnn"))!;
        Assert.Equal(["00030"], deLaAlerta.Sugerencias.Select(s => s.Diagnostico.Codigo));

        var intervenciones = d.Intervenciones.Where(i => i.Codigo is "3350" or "7710").Select(i => i.Id).ToList();
        var atendida = await enfermera.PostAsJsonAsync($"/api/v1/alertas/{alerta.Id}/atender",
            new { nota = "Semifowler, FR y SpO2 cada hora; se avisó al médico.", intervencionesEjecutadas = intervenciones });

        Assert.Equal(HttpStatusCode.OK, atendida.StatusCode);
        var ejecuciones = await Fabrica.ConsultarAsync(db => db.EjecucionesIntervencion.Where(e => e.AlertaId == alerta.Id).ToListAsync());
        Assert.Equal(intervenciones.Order(), ejecuciones.Select(e => e.PlanIntervencionId).Order());
    }

    [Fact]
    public async Task F8_atender_sin_el_campo_nuevo_se_comporta_como_antes_y_una_intervencion_ajena_da_400()
    {
        var (enfermera, ficha) = await PacienteE1Async();
        var alerta = (await enfermera.GetFromJsonAsync<List<AlertaDto>>($"/api/v1/pacientes/{ficha.Id}/alertas"))!.Single(a => a.Tipo == "riesgo");

        var ajena = await enfermera.PostAsJsonAsync($"/api/v1/alertas/{alerta.Id}/atender", new { nota = "Atendida.", intervencionesEjecutadas = new[] { Guid.NewGuid() } });
        Assert.Equal(HttpStatusCode.BadRequest, ajena.StatusCode);

        var comoAntes = await enfermera.PostAsJsonAsync($"/api/v1/alertas/{alerta.Id}/atender", new { nota = "Se reposicionó al paciente." });
        Assert.Equal(HttpStatusCode.OK, comoAntes.StatusCode);
    }

    [Fact]
    public async Task F8_el_egreso_resuelve_los_diagnosticos_activos_con_el_motivo_Egreso()
    {
        var (enfermera, ficha) = await PacienteE1Async();
        var d = await ConfirmadoAsync(enfermera, ficha.Id);

        Assert.Equal(HttpStatusCode.OK, (await enfermera.EgresarAsync(ficha.Id)).StatusCode);

        var plan = (await enfermera.GetFromJsonAsync<PlanCuidadosDto>($"/api/v1/pacientes/{ficha.Id}/plan-cuidados"))!;
        var cerrado = plan.Diagnosticos.Single(x => x.Id == d.Id);
        Assert.False(plan.HospitalizacionActiva);
        Assert.Equal(("Resuelto", "Egreso"), (cerrado.Estado, cerrado.MotivoCierre));
        Assert.DoesNotContain(
            await Fabrica.ConsultarAsync(db => db.SugerenciasEmitidas.Where(s => s.PacienteId == ficha.Id).ToListAsync()),
            s => s.Estado == EstadoSugerencia.Pendiente);
    }

    [Fact]
    public async Task F8_el_resumen_y_la_entrega_de_turno_traen_el_plan()
    {
        var (enfermera, ficha) = await PacienteE1Async();
        var d = await ConfirmadoAsync(enfermera, ficha.Id);
        var servicioId = await (await ClienteAdministradorAsync()).ServicioDePruebasAsync();

        using var resumen = JsonDocument.Parse(await enfermera.GetStringAsync($"/api/v1/pacientes/{ficha.Id}/resumen"));
        var plan = resumen.RootElement.GetProperty("planCuidados");
        var activo = Assert.Single(plan.GetProperty("diagnosticosActivos").EnumerateArray());
        Assert.Equal("00032", activo.GetProperty("codigo").GetString());
        Assert.Equal(1, plan.GetProperty("sugerenciasPendientes").GetInt32()); // 00030

        using var entrega = JsonDocument.Parse(await enfermera.GetStringAsync($"/api/v1/servicios/{servicioId}/entrega-turno"));
        var paciente = entrega.RootElement.GetProperty("pacientes").EnumerateArray().Single(p => p.GetProperty("pacienteId").GetGuid() == ficha.Id);
        var enEntrega = paciente.GetProperty("planCuidados");
        Assert.Equal("00032", enEntrega.GetProperty("diagnosticosActivos")[0].GetProperty("codigo").GetString());
        Assert.Equal(["3140"], enEntrega.GetProperty("intervencionesPendientes").EnumerateArray().Select(i => i.GetProperty("codigo").GetString()));
        Assert.Equal(["0415", "0802"], enEntrega.GetProperty("valoracionesPendientes").EnumerateArray().Select(v => v.GetProperty("codigo").GetString()).Order());
        Assert.Equal(d.Id, enEntrega.GetProperty("diagnosticosActivos")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task F8_el_catalogo_se_busca_sin_tildes_y_el_administrador_verifica_sin_que_la_semilla_lo_pise()
    {
        var enfermera = await ClienteEnfermeraAsync();
        var administrador = await ClienteAdministradorAsync();

        var resp = (await enfermera.GetFromJsonAsync<List<EntradaCatalogoDto>>("/api/v1/nnn/catalogo?tipo=nanda&q=resp"))!;
        Assert.Contains(resp, e => e.Codigo == "00032" && e.Edicion == "NANDA-I 2024-2026");
        Assert.Contains((await enfermera.GetFromJsonAsync<List<EntradaCatalogoDto>>("/api/v1/nnn/catalogo?tipo=nanda&q=PATRON"))!, e => e.Codigo == "00032");
        Assert.Equal(HttpStatusCode.BadRequest, (await enfermera.GetAsync("/api/v1/nnn/catalogo?tipo=otro")).StatusCode);

        var vinculos = (await enfermera.GetFromJsonAsync<VinculosDiagnosticoDto>("/api/v1/nnn/diagnosticos/00032/vinculos"))!;
        Assert.Equal(["3350", "3140", "6680"], vinculos.Intervenciones.Select(i => i.Codigo));
        Assert.Contains(vinculos.Resultados.Single(r => r.Codigo == "0802").Indicadores, i => i.Codigo == "080204" && i.ParametroVital == "fr");
        Assert.Equal(HttpStatusCode.NotFound, (await enfermera.GetAsync("/api/v1/nnn/diagnosticos/99999/vinculos")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await enfermera.PutAsync("/api/v1/nnn/catalogo/nanda/00146/verificacion", null)).StatusCode);
        var verificada = await administrador.PutAsync("/api/v1/nnn/catalogo/nanda/00146/verificacion", null);
        Assert.Equal(HttpStatusCode.OK, verificada.StatusCode);
        Assert.True((await verificada.Content.ReadFromJsonAsync<EntradaVerificadaDto>())!.Verificado);

        var semilla = await Fabrica.SembrarCatalogoNnnAsync();

        Assert.Equal(0, semilla.Agregadas);
        Assert.True(semilla.VerificadasSinTocar >= 1);
        Assert.True((await enfermera.GetFromJsonAsync<List<EntradaCatalogoDto>>("/api/v1/nnn/catalogo?tipo=nanda&q=ansiedad"))!.Single().Verificado);
    }

    [Fact]
    public async Task F8_los_indicadores_de_la_tesis_son_solo_agregados_y_del_administrador()
    {
        var (enfermera, ficha) = await PacienteE1Async();
        await ConfirmadoAsync(enfermera, ficha.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await enfermera.GetAsync("/api/v1/sistema/indicadores-nnn")).StatusCode);

        var texto = await (await ClienteAdministradorAsync()).GetStringAsync("/api/v1/sistema/indicadores-nnn");
        using var json = JsonDocument.Parse(texto);
        var sugerencias = json.RootElement.GetProperty("sugerencias");
        Assert.True(sugerencias.GetProperty("emitidas").GetInt32() >= 2);
        Assert.True(sugerencias.GetProperty("confirmadas").GetInt32() >= 1);
        Assert.Contains(sugerencias.GetProperty("porRegla").EnumerateArray(), r => r.GetProperty("regla").GetString() == "R1");
        Assert.DoesNotContain("pacienteId", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ficha.Id.ToString(), texto, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await (await ClienteAdministradorAsync()).GetAsync("/api/v1/sistema/indicadores-nnn?desde=2026-11-09T00:00:00Z&hasta=2026-11-01T00:00:00Z")).StatusCode);
    }

    [Fact]
    public async Task F8_ningun_log_auditoria_ni_evento_SignalR_contiene_un_codigo_ni_una_etiqueta_NANDA()
    {
        // Todo el flujo: sugerencias, confirmar, descartar, ejecutar, valorar, atender con intervenciones, resolver y egresar.
        var (enfermera, ficha) = await PacienteE1Async();
        await SugerenciasAsync(enfermera, ficha.Id);
        var d = await ConfirmadoAsync(enfermera, ficha.Id);
        await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/plan-cuidados/sugerencias/descartar",
            new { diagnosticoCodigo = "00030", reglaOrigen = "R2", motivo = "Artefacto del sensor." });
        await enfermera.PostAsJsonAsync($"/api/v1/plan-cuidados/intervenciones/{d.Intervenciones[0].Id}/ejecuciones", new { observacion = "Patrón respiratorio ineficaz 00032" });
        await enfermera.PostAsJsonAsync($"/api/v1/plan-cuidados/resultados/{d.Resultados[0].Id}/valoraciones", new { indicadores = new[] { new { codigo = "0415", puntuacion = 3 } } });
        var alerta = (await enfermera.GetFromJsonAsync<List<AlertaDto>>($"/api/v1/pacientes/{ficha.Id}/alertas"))!.Single(a => a.Tipo == "riesgo");
        await enfermera.PostAsJsonAsync($"/api/v1/alertas/{alerta.Id}/atender", new { nota = "Atendida con el plan.", intervencionesEjecutadas = new[] { d.Intervenciones[1].Id } });
        await enfermera.GetStringAsync($"/api/v1/alertas/{alerta.Id}/sugerencias-nnn");
        await enfermera.PutAsJsonAsync($"/api/v1/plan-cuidados/diagnosticos/{d.Id}/estado", new { estado = "Resuelto", motivo = "Mejoró la FR." });
        await enfermera.EgresarAsync(ficha.Id);

        var catalogo = SemillaNanda();
        var prohibido = new Regex(
            @"(?<![0-9A-Za-z\-])(" + string.Join('|', catalogo.Select(c => c.Codigo)) + @")(?![0-9A-Za-z\-])|" + string.Join('|', catalogo.Select(c => Regex.Escape(c.Etiqueta))),
            RegexOptions.IgnoreCase);
        var auditoria = await Fabrica.ConsultarAsync(db => db.Auditoria.Select(a => a.Detalle).ToListAsync());

        Assert.NotEmpty(Fabrica.Logs.Lineas);
        Assert.DoesNotContain(Fabrica.Logs.Lineas, prohibido.IsMatch);
        Assert.DoesNotContain(auditoria, a => a is not null && prohibido.IsMatch(a));
        Assert.Contains(Fabrica.TiempoReal.Eventos, e => e.Evento is "planActualizado" or "sugerenciasNnn");
        Assert.DoesNotContain(Fabrica.TiempoReal.Eventos, e => prohibido.IsMatch(e.Json));
    }

    /// <summary>Los diagnósticos NANDA-I de la semilla (código y etiqueta).</summary>
    private static IReadOnlyList<(string Codigo, string Etiqueta)> SemillaNanda() =>
        Infrastructure.Semillas.SemillaCatalogoNnn.LeerArchivo().Diagnosticos.Select(d => (d.Codigo, d.Etiqueta)).ToList();
}
