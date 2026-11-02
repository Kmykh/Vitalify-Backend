using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Clinica;
using Vitalify.Application.Pacientes;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Clinica;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>HU12 (fase 5) · Asistente de toma manual: presión, conciencia, oxígeno, próxima toma y avisos.</summary>
public class TomaManualTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    private async Task<(HttpClient Enfermera, FichaPacienteDto Ficha)> PacienteAsync()
    {
        var cama = await (await ClienteAdministradorAsync()).CrearCamaAsync();
        var enfermera = await ClienteEnfermeraAsync();
        return (enfermera, await enfermera.IngresarPacienteAsync(cama.Id));
    }

    private static Task<HttpResponseMessage> ObservarAsync(HttpClient cliente, Guid pacienteId, object toma) =>
        cliente.PostAsJsonAsync($"/api/v1/pacientes/{pacienteId}/observaciones", toma);

    private static async Task<ObservacionRegistradaDto> ObservarOkAsync(HttpClient cliente, Guid pacienteId, object toma)
    {
        var respuesta = await ObservarAsync(cliente, pacienteId, toma);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ObservacionRegistradaDto>())!;
    }

    private static object Lectura(int pas, int pad) => new { pas, pad };

    [Fact]
    public async Task TM_E1_ObservacionConDosLecturasDePresion_SeGuardaElPromedioRedondeadoYLasDosLecturas()
    {
        var (enfermera, ficha) = await PacienteAsync();

        var registrada = await ObservarOkAsync(enfermera, ficha.Id, new
        {
            presion = new { brazo = "Derecho", posicion = "Sentado", metodo = "Automatico", lecturas = new[] { Lectura(134, 86), Lectura(131, 83) } },
        });

        // (134 + 131) / 2 = 132,5 → 133; (86 + 83) / 2 = 84,5 → 85.
        Assert.Equal((int?)133, registrada.Riesgo.Parametros.Pas);
        var observacion = await Fabrica.ConsultarHistorialAsync(db => db.Observaciones.SingleAsync(o => o.Id == registrada.Id));
        Assert.Equal(((int?)133, (int?)85, (BrazoPresion?)BrazoPresion.Derecho), (observacion.Pas, observacion.Pad, observacion.BrazoPa));
        Assert.Equal([new LecturaPresion(134, 86, null), new LecturaPresion(131, 83, null)], observacion.LecturasPa!);

        var riesgo = (await enfermera.GetFromJsonAsync<RiesgoPacienteDto>($"/api/v1/pacientes/{ficha.Id}/riesgo"))!;
        Assert.Equal(("Derecho", "Escala1"), (riesgo.UltimoBrazoPa, riesgo.EscalaSpo2));
    }

    [Fact]
    public async Task TM_E2_DosLecturasQueDifierenEnMasDe10mmHgSinUnaTercera_400PidiendoUnaTercera()
    {
        var (enfermera, ficha) = await PacienteAsync();

        var respuesta = await ObservarAsync(enfermera, ficha.Id, new { presion = new { lecturas = new[] { Lectura(140, 80), Lectura(128, 80) } } });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var problema = (await respuesta.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Contains("tercera lectura", Assert.Single(problema.Errors["presion.lecturas"]));
        Assert.False(await Fabrica.ConsultarHistorialAsync(db => db.Observaciones.AnyAsync(o => o.PacienteId == ficha.Id)));

        // Con la tercera lectura se registra el promedio de las tres; la diastólica que difiere en 10 exactos no la pide.
        var registrada = await ObservarOkAsync(enfermera, ficha.Id, new { presion = new { lecturas = new[] { Lectura(140, 80), Lectura(128, 80), Lectura(131, 82) } } });
        Assert.Equal((int?)133, registrada.Riesgo.Parametros.Pas);
        Assert.Equal(HttpStatusCode.Created, (await ObservarAsync(enfermera, ficha.Id, new { presion = new { lecturas = new[] { Lectura(130, 90), Lectura(125, 80) } } })).StatusCode);
    }

    [Fact]
    public async Task TM_E3_ConRespuestasDeConciencia_ElBackendCalculaElAcvpuYGanaAunqueLaAppMandeOtroNivel()
    {
        var (enfermera, ficha) = await PacienteAsync();

        var registrada = await ObservarOkAsync(enfermera, ficha.Id, new
        {
            conciencia = new { nivel = "Alerta", despiertoCoherente = false, respondeVoz = true },
        });

        Assert.Equal("RespondeVoz", registrada.Riesgo.Parametros.Conciencia);
        Assert.Equal(3, registrada.Riesgo.News2.Puntos["conciencia"]);
        var observacion = await Fabrica.ConsultarHistorialAsync(db => db.Observaciones.SingleAsync(o => o.Id == registrada.Id));
        Assert.Equal((NivelConciencia?)NivelConciencia.RespondeVoz, observacion.Conciencia);

        // Respuestas incompletas: falta la siguiente pregunta de la cascada.
        var incompleta = await ObservarAsync(enfermera, ficha.Id, new { conciencia = new { despiertoCoherente = false, respondeVoz = false } });
        Assert.Equal(HttpStatusCode.BadRequest, incompleta.StatusCode);
        Assert.Contains("respondeDolor", (await incompleta.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors["conciencia"].Single());

        // El formato de la fase 4 (texto) se sigue aceptando.
        var plana = await ObservarOkAsync(enfermera, ficha.Id, new { conciencia = "ConfusionNueva", pas = 120, pad = 80, oxigenoSuplementario = false });
        Assert.Equal("ConfusionNueva", plana.Riesgo.Parametros.Conciencia);
    }

    [Fact]
    public async Task TM_E4_ProximaTomaManual_ConNews2CincoEsLaObservacionMasUnaHoraYConCeroMasDoce()
    {
        var (enfermera, ficha) = await PacienteAsync();
        var observadaEn = Ms(Fabrica.Reloj.AhoraUtc.AddMinutes(-10));

        // PAS 95 (2) + conciencia V (3) = 5 → medio.
        var cinco = await ObservarOkAsync(enfermera, ficha.Id, new
        {
            observadaEn,
            presion = new { lecturas = new[] { Lectura(95, 60) } },
            conciencia = new { despiertoCoherente = false, respondeVoz = true },
        });
        Assert.Equal((5, "medio"), (cinco.Riesgo.News2.Total, cinco.Riesgo.News2.Nivel));
        Assert.Equal(observadaEn.AddHours(1), cinco.Riesgo.ProximaTomaManualEn);

        // Otro paciente: todo normal → 0.
        var (_, otro) = await PacienteAsync();
        var cero = await ObservarOkAsync(enfermera, otro.Id, new
        {
            observadaEn,
            fr = 16,
            oxigeno = new { recibe = false },
            presion = new { lecturas = new[] { Lectura(120, 80) } },
            conciencia = new { despiertoCoherente = true, confusionNueva = false },
        });
        Assert.Equal(0, cero.Riesgo.News2.Total);
        Assert.Equal(observadaEn.AddHours(12), cero.Riesgo.ProximaTomaManualEn);
        Assert.Equal(cero.Riesgo.RespuestaSugerida, cero.Riesgo.Recomendacion);
    }

    [Fact]
    public async Task TM_E5_PreocupacionClinica_QuedaRegistradaYApareceEnLosAvisos()
    {
        var (enfermera, ficha) = await PacienteAsync();

        var registrada = await ObservarOkAsync(enfermera, ficha.Id, new { preocupacionClinica = true, fr = 16 });

        Assert.Contains(AvisosClinicos.PreocupacionClinica, registrada.Riesgo.Avisos);
        Assert.True((await Fabrica.ConsultarHistorialAsync(db => db.Observaciones.SingleAsync(o => o.Id == registrada.Id))).PreocupacionClinica);
        var riesgo = (await (await ClienteMedicoAsync()).GetFromJsonAsync<RiesgoPacienteDto>($"/api/v1/pacientes/{ficha.Id}/riesgo"))!;
        Assert.Contains("Preocupación clínica registrada: se notificará al médico.", riesgo.UltimaEvaluacion!.Avisos);
    }

    [Fact]
    public async Task LosAvisosAdviertenPresionExtremaChecklistIncompletoYObjetivo88a92EnEscala1()
    {
        var (enfermera, ficha) = await PacienteAsync();

        var registrada = await ObservarOkAsync(enfermera, ficha.Id, new
        {
            oxigeno = new { recibe = true, dispositivo = "Venturi", flujoLpm = 6, fio2 = 28, objetivo88a92 = true },
            presion = new
            {
                checklist = new { reposo5Min = true, brazoApoyadoAlturaCorazon = false, brazaleteTallaCorrecta = true, sinCafeTabacoEsfuerzo30Min = true, tensiometroValidado = true },
                lecturas = new[] { Lectura(88, 55), Lectura(86, 54) },
            },
            contexto = new { dolor = 7, ansiedadOEsfuerzoReciente = true },
        });

        Assert.Equal(
            [AvisosClinicos.PasBaja, AvisosClinicos.RevisarEscalaSpo2, AvisosClinicos.ChecklistPresionIncompleto],
            registrada.Riesgo.Avisos);
        var observacion = await Fabrica.ConsultarHistorialAsync(db => db.Observaciones.SingleAsync(o => o.Id == registrada.Id));
        Assert.Equal(((DispositivoOxigeno?)DispositivoOxigeno.Venturi, (decimal?)6m, (int?)28), (observacion.DispositivoOxigeno, observacion.FlujoOxigenoLpm, observacion.Fio2));
        Assert.Equal(7, observacion.Contexto!.Dolor);

        var muyAlta = await ObservarOkAsync(enfermera, ficha.Id, new { pas = 225, pad = 110 });
        Assert.Contains(AvisosClinicos.PasMuyAlta, muyAlta.Riesgo.Avisos);
    }

    [Fact]
    public async Task ElMedicoCambiaLaEscalaDeSpo2_SeAuditaYElRiesgoSeRecalculaAlInstante()
    {
        var (enfermera, ficha) = await PacienteAsync();
        await ObservarOkAsync(enfermera, ficha.Id, new { oxigeno = new { recibe = true, dispositivo = "CanulaNasal", flujoLpm = 2, objetivo88a92 = true } });
        var medico = await ClienteMedicoAsync();

        var respuesta = await medico.PutAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/escala-spo2", new { escala = "Escala2", motivo = "EPOC con hipercapnia; objetivo 88-92 %" });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var riesgo = (await respuesta.Content.ReadFromJsonAsync<RiesgoPacienteDto>())!;
        Assert.Equal(("Escala2", "CambioEscala", "Escala2"), (riesgo.EscalaSpo2, riesgo.UltimaEvaluacion!.Origen, riesgo.UltimaEvaluacion.Parametros.EscalaSpo2));
        Assert.DoesNotContain(AvisosClinicos.RevisarEscalaSpo2, riesgo.UltimaEvaluacion.Avisos);

        var auditoria = await Fabrica.ConsultarAsync(db => db.Auditoria.SingleAsync(a => a.Accion == AccionAuditoria.EscalaSpo2Cambiada && a.Detalle!.Contains(riesgo.HospitalizacionId.ToString())));
        Assert.DoesNotContain("EPOC", auditoria.Detalle!);

        Assert.Equal(HttpStatusCode.Forbidden, (await enfermera.PutAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/escala-spo2", new { escala = "Escala1", motivo = "x" })).StatusCode);
        var invalida = await medico.PutAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/escala-spo2", new { escala = "Escala3", motivo = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
    }

    [Fact]
    public async Task LaGuiaDeTomaManualEstaVersionadaYTraeSeccionesYFuentes()
    {
        var respuesta = await (await ClienteEnfermeraAsync()).GetAsync("/api/v1/guias/toma-manual");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("\"2026.11.1\"", respuesta.Headers.ETag?.Tag);
        using var guia = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("2026.11.1", guia.RootElement.GetProperty("version").GetString());
        var secciones = guia.RootElement.GetProperty("secciones");
        foreach (var seccion in new[] { "oxigeno", "conciencia", "presion", "frecuenciaRespiratoria", "contexto", "preocupacion" })
        {
            Assert.True(secciones.TryGetProperty(seccion, out var s), seccion);
            Assert.NotEmpty(s.GetProperty("recomendaciones").EnumerateArray());
        }

        var fuentes = guia.RootElement.GetProperty("fuentes");
        foreach (var fuente in new[] { "rm-031-2015-minsa", "gt-006-ins-cets-2026", "ley-27669", "nts-139-minsa-2018", "ley-29459", "news2-rcp-2017" })
        {
            Assert.StartsWith("https://", fuentes.GetProperty(fuente).GetProperty("enlace").GetString());
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClienteAdministradorAsync()).GetAsync("/api/v1/guias/toma-manual")).StatusCode);
    }

    [Fact]
    public async Task LaConfusionBasalSeEditaEnLaFichaDelPaciente()
    {
        var (enfermera, ficha) = await PacienteAsync();
        Assert.False(ficha.ConfusionBasal);

        var respuesta = await enfermera.PutAsJsonAsync($"/api/v1/pacientes/{ficha.Id}", new
        {
            nombreCompleto = ficha.NombreCompleto,
            fechaNacimiento = "1958-03-14",
            diagnosticoIngreso = "Neumonía adquirida en la comunidad",
            confusionBasal = true,
        });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True((await respuesta.Content.ReadFromJsonAsync<FichaPacienteDto>())!.ConfusionBasal);
        Assert.True((await enfermera.GetFromJsonAsync<FichaPacienteDto>($"/api/v1/pacientes/{ficha.Id}"))!.ConfusionBasal);
    }
}
