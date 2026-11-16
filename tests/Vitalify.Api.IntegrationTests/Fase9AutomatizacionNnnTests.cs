using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Nnn;
using Vitalify.Application.Notas;
using Vitalify.Application.Pacientes;
using Vitalify.Domain.Notas;

namespace Vitalify.Api.IntegrationTests;

/// <summary>
/// Fase 9: automatización asistida del PAE. El paquete se confirma con un toque, la toma manual completa registra la NIC 6680,
/// el plan trae la agenda, la valoración del turno va en lote, "Mi turno" ordena por riesgo, los recordatorios llevan solo
/// cantidades y la nota del turno se firma a partir de su borrador. La enfermera sigue decidiendo.
/// </summary>
public sealed class Fase9AutomatizacionNnnTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    private static int _seq = 900_000;

    private static readonly object TomaCompleta = new { fr = 24, pas = 118, pad = 76, conciencia = "Alerta", oxigenoSuplementario = false };

    /// <summary>FR 26 y SpO2 90 del wearable (3 puntos cada uno) desde hace 70 s: persisten lo suficiente para sugerirse.</summary>
    private async Task<(HttpClient Enfermera, FichaPacienteDto Ficha)> PacienteGraveAsync()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        using (Fabrica.Reloj.Adelantar(TimeSpan.FromSeconds(-70)))
        {
            await administrador.RegistrarTelemetriaAsync(new
            {
                dispositivo = sensor.Codigo, ts = ClienteApi.Ts(Fabrica.Reloj.AhoraUtc), seq = Interlocked.Increment(ref _seq), fc = 80, fr = 26, spo2 = 90,
                caida = false,
            });
        }

        return (enfermera, ficha);
    }

    private static async Task<DiagnosticoPlanDto> ConfirmarPaqueteAsync(HttpClient enfermera, Guid pacienteId, string codigo = "00032")
    {
        var respuesta = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{pacienteId}/plan-cuidados/sugerencias/confirmar-paquete", new { diagnosticoCodigo = codigo });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<DiagnosticoPlanDto>())!;
    }

    private static async Task<DiagnosticoPlanDto> ConfirmarConSignosAsync(HttpClient enfermera, Guid pacienteId)
    {
        var respuesta = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{pacienteId}/plan-cuidados/diagnosticos", new
        {
            diagnosticoCodigo = "00240", origen = "Manual",
            intervenciones = new[] { new { codigo = "6680", frecuencia = "PorProtocolo" } },
            resultados = new[] { new { codigo = "0802", puntuacionBasal = 2, puntuacionMeta = 4 } },
        });
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<DiagnosticoPlanDto>())!;
    }

    private static async Task<PlanCuidadosDto> PlanAsync(HttpClient cliente, Guid pacienteId) =>
        (await cliente.GetFromJsonAsync<PlanCuidadosDto>($"/api/v1/pacientes/{pacienteId}/plan-cuidados"))!;

    [Fact]
    public async Task F9_la_sugerencia_trae_su_paquete_y_se_confirma_con_un_toque()
    {
        var (enfermera, ficha) = await PacienteGraveAsync();

        var sugerencias = (await enfermera.GetFromJsonAsync<SugerenciasPacienteDto>($"/api/v1/pacientes/{ficha.Id}/nnn/sugerencias"))!;
        var patron = sugerencias.Sugerencias.Single(s => s.Diagnostico.Codigo == "00032");
        Assert.True(patron.PorConfirmar); // solo del wearable: la app ofrece tomar la FR a mano
        Assert.Equal(["fr"], patron.ConfirmarCon);
        Assert.Equal("2026.12.1", patron.Paquete!.VersionProtocolo);
        Assert.Equal(["PorProtocolo", "PorProtocolo", "UnaVez"], patron.Paquete.Intervenciones.Select(i => i.Frecuencia));
        var noc = Assert.Single(patron.Paquete.Resultados);
        Assert.Equal(("0415", 1, 4, true), (noc.Codigo, noc.PuntuacionBasal, noc.PuntuacionMeta, noc.BasalDesdeSignos)); // FR 26 → 3 puntos → 1

        var d = await ConfirmarPaqueteAsync(enfermera, ficha.Id);
        Assert.Equal(("Activo", true, "2026.12.1"), (d.Estado, d.ConfirmadoSinCambios, d.VersionProtocolo));
        Assert.Equal("PorProtocolo", d.Intervenciones.Single(i => i.Codigo == "3350").Frecuencia);

        // Confirmada con cambios en la confirmación completa: no cuenta como "sin cambios".
        var editada = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/plan-cuidados/diagnosticos", new
        {
            diagnosticoCodigo = "00030", origen = "Sugerido", reglaOrigen = "R2",
            intervenciones = new[] { new { codigo = "3320", frecuencia = "CadaHora" } },
            resultados = new[] { new { codigo = "0402", puntuacionBasal = 1, puntuacionMeta = 3 } },
        });
        Assert.Equal(HttpStatusCode.Created, editada.StatusCode);
        Assert.False((await editada.Content.ReadFromJsonAsync<DiagnosticoPlanDto>())!.ConfirmadoSinCambios);

        var noSugerida = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/plan-cuidados/sugerencias/confirmar-paquete", new { diagnosticoCodigo = "00132" });
        Assert.Equal(HttpStatusCode.NotFound, noSugerida.StatusCode);
        Assert.Equal("sugerencia-no-encontrada", (await noSugerida.Content.ReadFromJsonAsync<ProblemDetails>())!.Type);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClienteMedicoAsync()).PostAsJsonAsync(
            $"/api/v1/pacientes/{ficha.Id}/plan-cuidados/sugerencias/confirmar-paquete", new { diagnosticoCodigo = "00032" })).StatusCode);
    }

    [Fact]
    public async Task F9_una_toma_manual_completa_registra_la_6680_a_nombre_de_la_enfermera_y_una_incompleta_no()
    {
        var (enfermera, ficha) = await PacienteGraveAsync();
        await ConfirmarConSignosAsync(enfermera, ficha.Id);

        Assert.Equal(HttpStatusCode.Created, (await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/observaciones", TomaCompleta)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/observaciones", new { fr = 20 })).StatusCode);

        var plan = await PlanAsync(enfermera, ficha.Id);
        var signos = plan.Diagnosticos.Single(d => d.Codigo == "00240").Intervenciones.Single(i => i.Codigo == "6680");
        var ejecucion = Assert.Single(signos.Ejecuciones);
        Assert.Equal("TomaManual", ejecucion.Origen);
        Assert.Equal("Enfermera", ejecucion.EjecutadaPor.Rol);
        Assert.NotNull(signos.ProximaEn); // misma hora que la próxima toma manual
        Assert.NotNull(plan.NivelRespuesta);
    }

    [Fact]
    public async Task F9_el_plan_trae_la_agenda_y_la_NIC_de_una_vez_toca_enseguida()
    {
        var (enfermera, ficha) = await PacienteGraveAsync();
        await ConfirmarPaqueteAsync(enfermera, ficha.Id);

        var plan = await PlanAsync(enfermera, ficha.Id);
        Assert.Equal("medio", plan.NivelRespuesta); // NEWS2 6: respuesta urgente, monitorizar cada hora
        var intervenciones = plan.Diagnosticos.Single(d => d.Codigo == "00032").Intervenciones;
        Assert.Equal("vencida", intervenciones.Single(i => i.Codigo == "7710").Agenda);
        Assert.All(intervenciones, i => Assert.NotNull(i.Agenda));
        Assert.Null(plan.Diagnosticos.Single().ListoParaResolver); // recién confirmado: nunca
    }

    [Fact]
    public async Task F9_la_valoracion_del_turno_se_propone_y_se_guarda_en_lote()
    {
        var (enfermera, ficha) = await PacienteGraveAsync();
        var d = await ConfirmarPaqueteAsync(enfermera, ficha.Id);

        var turno = (await enfermera.GetFromJsonAsync<ValoracionTurnoDto>($"/api/v1/pacientes/{ficha.Id}/plan-cuidados/valoracion-turno"))!;
        var resultado = Assert.Single(turno.Resultados);
        Assert.False(resultado.ValoradoEnGuardia);
        Assert.Equal(1, resultado.Indicadores.Single(i => i.Codigo == "0415").PuntuacionSugerida);

        var guardado = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/plan-cuidados/valoracion-turno", new
        {
            resultados = new[] { new { resultadoId = resultado.PlanResultadoId, indicadores = new[] { new { codigo = "0415", puntuacion = 2 } } } },
        });
        Assert.Equal(HttpStatusCode.Created, guardado.StatusCode);
        Assert.Equal(1, (await guardado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("valoraciones").GetInt32());

        var valoracion = Assert.Single((await PlanAsync(enfermera, ficha.Id)).Diagnosticos.Single(x => x.Id == d.Id).Resultados.Single().Valoraciones);
        Assert.Equal((2, (int?)1), (valoracion.Puntuacion, valoracion.PuntuacionSugerida));
        Assert.True((await enfermera.GetFromJsonAsync<ValoracionTurnoDto>($"/api/v1/pacientes/{ficha.Id}/plan-cuidados/valoracion-turno"))!.Resultados.Single().ValoradoEnGuardia);

        var ajeno = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/plan-cuidados/valoracion-turno", new
        {
            resultados = new[] { new { resultadoId = Guid.NewGuid(), indicadores = new[] { new { codigo = "0415", puntuacion = 2 } } } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, ajeno.StatusCode);
    }

    [Fact]
    public async Task F9_mi_turno_trae_al_paciente_con_su_alerta_lo_vencido_y_lo_que_falta()
    {
        var (enfermera, ficha) = await PacienteGraveAsync();
        await ConfirmarPaqueteAsync(enfermera, ficha.Id);

        var panel = (await enfermera.GetFromJsonAsync<PanelTurnoDto>("/api/v1/mi-turno"))!;
        var paciente = panel.Pacientes.Single(p => p.PacienteId == ficha.Id);

        Assert.NotNull(panel.Guardia);
        Assert.Equal("medio", paciente.NivelRespuesta);
        Assert.Equal("riesgo", paciente.Alerta!.Tipo);
        Assert.Contains(paciente.Vencidas, v => v.Codigo == "7710");
        Assert.True(paciente.SugerenciasPendientes >= 1); // 00030
        Assert.True(paciente.ValoracionesPendientes >= 1);
        Assert.False(paciente.NotaTurnoFirmada);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClienteAdministradorAsync()).GetAsync("/api/v1/mi-turno")).StatusCode);
    }

    [Fact]
    public async Task F9_la_nota_del_turno_se_redacta_en_los_cinco_pasos_del_PAE_y_se_firma_una_vez_por_guardia()
    {
        var (enfermera, ficha) = await PacienteGraveAsync();
        await ConfirmarPaqueteAsync(enfermera, ficha.Id);

        var borrador = (await enfermera.GetFromJsonAsync<BorradorNotaTurnoDto>($"/api/v1/pacientes/{ficha.Id}/nota-turno/borrador"))!;
        Assert.Contains("1. Valoración", borrador.Texto, StringComparison.Ordinal);
        Assert.Contains("5. Evaluación", borrador.Texto, StringComparison.Ordinal);
        Assert.Contains("00032", borrador.Texto, StringComparison.Ordinal);
        Assert.Contains("NEWS2", borrador.Texto, StringComparison.Ordinal);
        Assert.Contains(RedactorNotaPae.AvisoSensor, borrador.Texto, StringComparison.Ordinal);
        Assert.Null(borrador.NotaFirmadaId);

        var firmada = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/nota-turno", new { texto = borrador.Texto + "\nPaciente tranquilo." });
        Assert.Equal(HttpStatusCode.Created, firmada.StatusCode);
        var nota = (await firmada.Content.ReadFromJsonAsync<NotaDto>())!;
        Assert.Equal("ProcesoEnfermeria", nota.Tipo);

        var otra = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/nota-turno", new { texto = "Otra nota del mismo turno." });
        Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
        Assert.Equal("nota-turno-ya-registrada", (await otra.Content.ReadFromJsonAsync<ProblemDetails>())!.Type);
        Assert.Equal(nota.Id, (await enfermera.GetFromJsonAsync<BorradorNotaTurnoDto>($"/api/v1/pacientes/{ficha.Id}/nota-turno/borrador"))!.NotaFirmadaId);

        // La API genérica de notas no crea notas del PAE, y el médico no firma la de enfermería.
        Assert.Equal(HttpStatusCode.BadRequest, (await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/notas", new { tipo = "ProcesoEnfermeria", texto = "Nota del PAE." })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClienteMedicoAsync()).PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/nota-turno", new { texto = "Nota del PAE." })).StatusCode);
    }

    [Fact]
    public async Task F9_los_recordatorios_avisan_lo_vencido_solo_con_cantidades_y_agrupan_el_push()
    {
        var (enfermera, ficha) = await PacienteGraveAsync();
        var token = $"tok-recordatorio-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.NoContent, (await enfermera.PostAsJsonAsync("/api/v1/notificaciones/dispositivos", new { token, plataforma = "Android" })).StatusCode);
        var desde = Fabrica.Reloj.AhoraUtc.AddSeconds(-1);
        await ConfirmarPaqueteAsync(enfermera, ficha.Id); // la NIC 7710 (una vez) toca enseguida

        var antes = Fabrica.TiempoReal.De("recordatoriosPlan").Count;
        await Fabrica.VigilarPlanAsync(desde);

        var evento = Fabrica.TiempoReal.De("recordatoriosPlan").Skip(antes).Single(e => e.Json.Contains(ficha.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains("\"motivo\":\"vencidas\"", evento.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("00032", evento.Json, StringComparison.Ordinal);
        Assert.DoesNotContain("7710", evento.Json, StringComparison.Ordinal);

        var push = Fabrica.Push.Enviados.Where(e => e.Tokens.Contains(token) && e.Mensaje.Datos.GetValueOrDefault("tipo") == "recordatorio-plan").ToList();
        var agrupado = Assert.Single(push);
        Assert.DoesNotContain("00032", agrupado.Mensaje.Cuerpo, StringComparison.Ordinal);

        // Otra vuelta enseguida: el push agrupado espera 30 min.
        await Fabrica.VigilarPlanAsync(desde);
        Assert.Single(Fabrica.Push.Enviados, e => e.Tokens.Contains(token) && e.Mensaje.Datos.GetValueOrDefault("tipo") == "recordatorio-plan");
    }

    [Fact]
    public async Task F9_los_indicadores_traen_la_automatizacion()
    {
        var (enfermera, ficha) = await PacienteGraveAsync();
        await ConfirmarPaqueteAsync(enfermera, ficha.Id);
        await ConfirmarConSignosAsync(enfermera, ficha.Id);
        await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/observaciones", TomaCompleta);

        var texto = await (await ClienteAdministradorAsync()).GetStringAsync("/api/v1/sistema/indicadores-nnn");
        using var json = JsonDocument.Parse(texto);
        var automatizacion = json.RootElement.GetProperty("automatizacion");
        Assert.True(automatizacion.GetProperty("paquetesConfirmados").GetInt32() >= 1);
        Assert.True(automatizacion.GetProperty("confirmadosSinCambios").GetInt32() >= 1);
        Assert.True(automatizacion.GetProperty("ejecucionesPorTomaManual").GetInt32() >= 1);
        Assert.DoesNotContain(ficha.Id.ToString(), texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task F9_OpenAPI_trae_ejemplos_de_cada_endpoint_nuevo()
    {
        using var swagger = JsonDocument.Parse(await Fabrica.CrearCliente().GetStringAsync("/swagger/v1/swagger.json"));
        var rutas = swagger.RootElement.GetProperty("paths");
        var sinEjemplo = new List<string>();
        foreach (var (metodo, ruta) in new[]
        {
            ("get", "/api/v1/mi-turno"), ("post", "/api/v1/pacientes/{id}/plan-cuidados/sugerencias/confirmar-paquete"),
            ("get", "/api/v1/pacientes/{id}/plan-cuidados/valoracion-turno"), ("post", "/api/v1/pacientes/{id}/plan-cuidados/valoracion-turno"),
            ("get", "/api/v1/pacientes/{id}/nota-turno/borrador"), ("post", "/api/v1/pacientes/{id}/nota-turno"),
        })
        {
            var operacion = rutas.GetProperty(ruta).GetProperty(metodo);
            var exito = operacion.GetProperty("responses").EnumerateObject().First(r => r.Name.StartsWith('2')).Value;
            var conEjemplo = exito.GetProperty("content").EnumerateObject().Any(c => c.Value.TryGetProperty("example", out _));
            if (operacion.TryGetProperty("requestBody", out var cuerpo))
            {
                conEjemplo &= cuerpo.GetProperty("content").EnumerateObject().Any(c => c.Value.TryGetProperty("example", out _));
            }

            if (!conEjemplo)
            {
                sinEjemplo.Add($"{metodo} {ruta}");
            }
        }

        Assert.Empty(sinEjemplo);
    }
}
