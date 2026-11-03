using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.Contratos;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Alertas;
using Vitalify.Application.Auditoria;
using Vitalify.Application.Historial;
using Vitalify.Application.Notas;
using Vitalify.Domain.Auditoria;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>Fase 7: historial del paciente para el médico (HU19), notas clínicas (HU22) y auditoría para el administrador.</summary>
public class Fase7HistorialTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    private long _seq;

    /// <summary>Una lectura que llega en tiempo real hace <paramref name="hace"/> (el riesgo se evalúa con la hora de la API).</summary>
    private async Task EnviarHaceAsync(HttpClient administrador, string sensor, TimeSpan hace, int fc, int fr, int spo2, bool caida = false)
    {
        using (Fabrica.Reloj.Adelantar(-hace))
        {
            await administrador.RegistrarTelemetriaAsync(new
            {
                dispositivo = sensor, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = Interlocked.Increment(ref _seq), fc, fr, spo2, caida,
            });
        }
    }

    private async Task<(HttpClient Cliente, Guid Id)> MedicoDeOtroServicioAsync()
    {
        var servicio = await (await ClienteAdministradorAsync()).CrearServicioAsync("Cirugía");
        var (cliente, sesion) = await ClienteConRolAsync("Medico", servicio.Id);
        return (cliente, sesion.Usuario.Id);
    }

    private Task<int> ConsultasAuditadasAsync(Guid usuarioId) =>
        Fabrica.ConsultarAsync(db => db.Auditoria.CountAsync(a => a.Accion == AccionAuditoria.ConsultaHistorialPaciente && a.UsuarioId == usuarioId));

    [Fact]
    public async Task HU19_E1_UnMedicoDeOtroServicioVeLaLineaDeTiempoDeSignosRiesgoYAlertas_YQuedaAuditado()
    {
        var (_, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        await EnviarHaceAsync(administrador, sensor.Codigo, TimeSpan.FromMinutes(30), fc: 80, fr: 16, spo2: 97);
        await EnviarHaceAsync(administrador, sensor.Codigo, TimeSpan.FromMinutes(20), fc: 95, fr: 25, spo2: 91); // NEWS2 7
        await EnviarHaceAsync(administrador, sensor.Codigo, TimeSpan.FromMinutes(10), fc: 82, fr: 16, spo2: 97);
        var (medico, medicoId) = await MedicoDeOtroServicioAsync();

        var signos = (await medico.GetFromJsonAsync<HistorialSignosDto>($"/api/v1/pacientes/{ficha.Id}/signos/historial"))!;
        var riesgo = (await medico.GetFromJsonAsync<HistorialRiesgoDto>($"/api/v1/pacientes/{ficha.Id}/riesgo/historial"))!;
        var alertas = (await medico.GetFromJsonAsync<List<AlertaDto>>($"/api/v1/pacientes/{ficha.Id}/alertas"))!;

        Assert.Equal([80, 95, 82], signos.Lecturas.Select(l => l.Fc));
        Assert.Equal([0, 7, 0], riesgo.Evaluaciones.Select(e => e.News2.Total));
        var alto = riesgo.Evaluaciones[1];
        Assert.Equal(("alto", false, 3), (alto.News2.Nivel, alto.News2.Completo, alto.News2.Puntos["fr"]));
        Assert.Contains("pas", alto.News2.Faltantes);
        var alerta = Assert.Single(alertas);
        Assert.Equal(("riesgo", "alta"), (alerta.Tipo, alerta.Prioridad));
        Assert.Equal(3, await ConsultasAuditadasAsync(medicoId));
    }

    [Fact]
    public async Task HU19_E2_ElFiltroPorFechasYAgruparSeResuelvenEnLaBase()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        var ahora = Fabrica.Reloj.AhoraUtc;
        var inicio = new DateTime(ahora.AddHours(-2).Ticks - (ahora.AddHours(-2).Ticks % TimeSpan.FromMinutes(5).Ticks), DateTimeKind.Utc);
        await EnviarHaceAsync(administrador, sensor.Codigo, ahora - inicio.AddMinutes(1), fc: 70, fr: 16, spo2: 97);
        await EnviarHaceAsync(administrador, sensor.Codigo, ahora - inicio.AddMinutes(2), fc: 80, fr: 18, spo2: 95); // NEWS2 1 (SpO2 94–95)
        await EnviarHaceAsync(administrador, sensor.Codigo, ahora - inicio.AddMinutes(6), fc: 95, fr: 25, spo2: 91, caida: true); // NEWS2 7
        await EnviarHaceAsync(administrador, sensor.Codigo, ahora - inicio.AddMinutes(20), fc: 100, fr: 16, spo2: 97);
        var rango = $"desde={Ts(inicio)}&hasta={Ts(inicio.AddMinutes(10))}";

        var crudo = (await enfermera.GetFromJsonAsync<HistorialSignosDto>($"/api/v1/pacientes/{ficha.Id}/signos/historial?{rango}"))!;
        var signos = (await enfermera.GetFromJsonAsync<HistorialSignosDto>($"/api/v1/pacientes/{ficha.Id}/signos/historial?{rango}&agrupar=5m"))!;
        var riesgo = (await enfermera.GetFromJsonAsync<HistorialRiesgoDto>($"/api/v1/pacientes/{ficha.Id}/riesgo/historial?{rango}&agrupar=5m"))!;

        Assert.Equal([70, 80, 95], crudo.Lecturas.Select(l => l.Fc));
        Assert.Empty(signos.Lecturas);
        Assert.Equal(("5m", "5m"), (signos.Agrupar, riesgo.Agrupar));
        Assert.Equal([inicio, inicio.AddMinutes(5)], signos.Grupos!.Select(g => g.Inicio));
        var primero = signos.Grupos![0];
        Assert.Equal((2, 75.0m, 70m, 80m, false), (primero.Lecturas, primero.Fc!.Promedio, primero.Fc.Minimo, primero.Fc.Maximo, primero.Caida));
        Assert.Equal((1, 95m, true), (signos.Grupos[1].Lecturas, signos.Grupos[1].Fc!.Maximo, signos.Grupos[1].Caida));
        Assert.Null(primero.Temperatura);
        Assert.Equal([2, 1], riesgo.Grupos!.Select(g => g.Evaluaciones));
        Assert.Equal((0, 1, "bajo"), (riesgo.Grupos![0].News2.Minimo, riesgo.Grupos[0].News2.Maximo, riesgo.Grupos[0].News2.NivelMaximo));
        Assert.Equal((7, 7, "alto"), (riesgo.Grupos[1].News2.Maximo, riesgo.Grupos[1].News2.Ultimo, riesgo.Grupos[1].News2.NivelMaximo));
    }

    [Fact]
    public async Task UnRangoDeMasDe7DiasSinAgrupar_UnAgruparInvalido_YUnEpisodioAjeno_SeRechazan()
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();
        var (_, otro, _, _) = await PacienteConSensorAsync();
        var ahora = Fabrica.Reloj.AhoraUtc;

        var largo = await enfermera.GetAsync($"/api/v1/pacientes/{ficha.Id}/signos/historial?desde={Ts(ahora.AddDays(-8))}&hasta={Ts(ahora)}");
        var agruparMalo = await enfermera.GetAsync($"/api/v1/pacientes/{ficha.Id}/signos/historial?agrupar=2h");
        var riesgoPorMinuto = await enfermera.GetAsync($"/api/v1/pacientes/{ficha.Id}/riesgo/historial?agrupar=1m");
        var ajeno = await enfermera.GetAsync($"/api/v1/pacientes/{ficha.Id}/signos/historial?hospitalizacionId={otro.HospitalizacionActiva!.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, largo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, agruparMalo.StatusCode);
        Assert.Contains("agrupar", (await riesgoPorMinuto.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>())!.Errors.Keys);
        Assert.Equal((HttpStatusCode.NotFound, "hospitalizacion-no-encontrada"), (ajeno.StatusCode, (await ajeno.LeerProblemaAsync()).Type));
    }

    [Fact]
    public async Task LasHospitalizacionesMuestranCadaEpisodioConCamaServicioEgresoYSensores()
    {
        var (enfermera, ficha, sensor, cama) = await PacienteConSensorAsync();
        Assert.Equal(HttpStatusCode.OK, (await enfermera.EgresarAsync(ficha.Id, "Traslado")).StatusCode);
        var otraCama = await (await ClienteAdministradorAsync()).CrearCamaAsync();
        Assert.Equal(HttpStatusCode.Created, (await enfermera.IngresarAsync(otraCama.Id, ficha.NumeroDocumento)).StatusCode);
        var (medico, _) = await MedicoDeOtroServicioAsync();

        var episodios = (await medico.GetFromJsonAsync<HospitalizacionesPacienteDto>($"/api/v1/pacientes/{ficha.Id}/hospitalizaciones"))!.Episodios;

        Assert.Equal(2, episodios.Count);
        Assert.Equal(("Activa", otraCama.Codigo), (episodios[0].Estado, episodios[0].Cama.Codigo));
        var anterior = episodios[1];
        Assert.Equal(("Finalizada", "Traslado", cama.Codigo, cama.ServicioId), (anterior.Estado, anterior.MotivoEgreso, anterior.Cama.Codigo, anterior.Servicio.Id));
        Assert.NotNull(anterior.EgresoEn);
        var usado = Assert.Single(anterior.Sensores);
        Assert.Equal((sensor.Codigo, "Egreso"), (usado.Codigo, usado.MotivoLiberacion));
    }

    [Fact]
    public async Task LasObservacionesTraenLaTomaCompletaYElNombreDeQuienLaRegistro()
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();
        Assert.Equal(HttpStatusCode.Created, (await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/observaciones",
            new { fr = 20, pas = 130, pad = 85, conciencia = "Alerta", oxigenoSuplementario = false, preocupacionClinica = false })).StatusCode);
        var medico = await ClienteMedicoAsync();

        var observacion = Assert.Single((await medico.GetFromJsonAsync<ObservacionesPacienteDto>($"/api/v1/pacientes/{ficha.Id}/observaciones"))!.Observaciones);

        Assert.Equal(("Enfermera de prueba", "Enfermera"), (observacion.RegistradaPor.Nombre, observacion.RegistradaPor.Rol));
        Assert.Equal((20, "Alerta", 130, 85, false), (observacion.Fr, observacion.Conciencia, observacion.Presion.Pas, observacion.Presion.Pad, observacion.Oxigeno.Recibe));
    }

    [Fact]
    public async Task ElResumenJuntaPacienteEpisodioRiesgoExtremosAlertasNotasYProximaToma()
    {
        var (enfermera, ficha, sensor, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();
        await EnviarHaceAsync(administrador, sensor.Codigo, TimeSpan.FromMinutes(20), fc: 70, fr: 16, spo2: 97);
        await EnviarHaceAsync(administrador, sensor.Codigo, TimeSpan.FromMinutes(10), fc: 95, fr: 25, spo2: 91); // NEWS2 7
        Assert.Equal(HttpStatusCode.Created, (await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/observaciones",
            new { fr = 24, pas = 100, pad = 60, conciencia = "Alerta", oxigenoSuplementario = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/notas",
            new { tipo = "Enfermeria", texto = "Paciente taquipneico, se avisa al médico." })).StatusCode);
        var (medico, medicoId) = await MedicoDeOtroServicioAsync();

        var resumen = (await medico.GetFromJsonAsync<ResumenPacienteDto>($"/api/v1/pacientes/{ficha.Id}/resumen"))!;

        Assert.Equal((ficha.Id, ficha.NombreCompleto, "Activa"), (resumen.Paciente.Id, resumen.Paciente.NombreCompleto, resumen.Episodio.Estado));
        Assert.Equal((70m, 95m, 2), (resumen.Extremos.Sensor.Fc!.Minimo, resumen.Extremos.Sensor.Fc.Maximo, resumen.Extremos.Sensor.Fc.Mediciones));
        Assert.Equal((100m, 24m), (resumen.Extremos.TomaManual.Pas!.Minimo, resumen.Extremos.TomaManual.Fr!.Maximo));
        Assert.Equal(5, resumen.Alertas.Count);
        Assert.Equal((1, 1), (resumen.Alertas.Single(a => a.Tipo == "riesgo").Cantidad, resumen.Alertas.Single(a => a.Tipo == "riesgo").Abiertas));
        Assert.Equal("riesgo", Assert.Single(resumen.AlertasAbiertas).Tipo);
        Assert.Equal("Paciente taquipneico, se avisa al médico.", Assert.Single(resumen.UltimasNotas).Texto);
        Assert.NotNull(resumen.UltimoRiesgo);
        Assert.Equal(resumen.UltimoRiesgo!.ProximaTomaManualEn, resumen.ProximaTomaManualEn);
        Assert.False(resumen.TomaVencida);
        Assert.Equal(1, await ConsultasAuditadasAsync(medicoId));
    }

    [Fact]
    public async Task HU22_E1_LaEnfermeraRegistraUnaNotaQueQuedaConFechaHoraYSuNombre()
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();

        var respuesta = await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/notas",
            new { tipo = "Enfermeria", texto = "Se cambia vía periférica sin complicaciones." });
        var nota = (await respuesta.Content.ReadFromJsonAsync<NotaDto>())!;
        var (medico, _) = await MedicoDeOtroServicioAsync();
        var lista = (await medico.GetFromJsonAsync<NotasPacienteDto>($"/api/v1/pacientes/{ficha.Id}/notas"))!;

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        Assert.Equal(("Enfermeria", "Enfermera de prueba", 1), (nota.Tipo, nota.Autor.Nombre, nota.Version));
        Assert.True(Math.Abs((nota.CreadaEn - Fabrica.Reloj.AhoraUtc).TotalMinutes) < 1);
        Assert.Equal(nota.Id, Assert.Single(lista.Notas).Id);
        var auditada = await Fabrica.ConsultarAsync(db => db.Auditoria.SingleAsync(a => a.Accion == AccionAuditoria.NotaRegistrada && a.Detalle!.Contains(nota.Id.ToString())));
        Assert.DoesNotContain("periférica", auditada.Detalle);
    }

    [Fact]
    public async Task HU22_E2_ElAutorLaCorrigeDentroDe24h_SeConservaLaVersionAnterior_YNadieLaBorra()
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();
        var nota = (await (await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/notas",
            new { tipo = "Evolucion", texto = "Texto original de la nota." })).Content.ReadFromJsonAsync<NotaDto>())!;
        var otra = await ClienteEnfermeraAsync();

        var editada = await enfermera.PutAsJsonAsync($"/api/v1/notas/{nota.Id}", new { texto = "Texto corregido de la nota." });
        var ajena = await otra.PutAsJsonAsync($"/api/v1/notas/{nota.Id}", new { texto = "Corrección de otra enfermera." });
        HttpResponseMessage tardia;
        using (Fabrica.Reloj.Adelantar(TimeSpan.FromHours(25)))
        {
            tardia = await enfermera.PutAsJsonAsync($"/api/v1/notas/{nota.Id}", new { texto = "Corrección después de 24 h." });
        }

        var versiones = (await otra.GetFromJsonAsync<VersionesNotaDto>($"/api/v1/notas/{nota.Id}/versiones"))!;
        var borrar = await enfermera.DeleteAsync($"/api/v1/notas/{nota.Id}");

        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        var dto = (await editada.Content.ReadFromJsonAsync<NotaDto>())!;
        Assert.Equal(("Texto corregido de la nota.", 2), (dto.Texto, dto.Version));
        Assert.NotNull(dto.EditadaEn);
        Assert.Equal((HttpStatusCode.Forbidden, "nota-de-otro-autor"), (ajena.StatusCode, (await ajena.LeerProblemaAsync()).Type));
        Assert.Equal((HttpStatusCode.Conflict, "nota-fuera-de-plazo"), (tardia.StatusCode, (await tardia.LeerProblemaAsync()).Type));
        Assert.Equal(2, versiones.VersionActual);
        var anterior = Assert.Single(versiones.Versiones);
        Assert.Equal((1, "Texto original de la nota."), (anterior.Numero, anterior.Texto));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, borrar.StatusCode);
    }

    [Fact]
    public async Task LaAuditoriaMuestraAlAdministradorSoloAccionesEIds_SinElMotivoDelEgreso()
    {
        var (enfermera, ficha, _, _) = await PacienteConSensorAsync();
        var (medico, medicoId) = await MedicoDeOtroServicioAsync();
        await medico.GetAsync($"/api/v1/pacientes/{ficha.Id}/resumen");
        Assert.Equal(HttpStatusCode.OK, (await enfermera.EgresarAsync(ficha.Id, "Fallecimiento")).StatusCode);
        var administrador = await ClienteAdministradorAsync();

        var consultas = (await administrador.GetFromJsonAsync<PaginaRespuesta<AuditoriaDto>>(
            $"/api/v1/auditoria?accion=ConsultaHistorialPaciente&usuarioId={medicoId}"))!;
        var egresos = await administrador.GetStringAsync($"/api/v1/auditoria?accion=PacienteEgresado&tamano=100");

        var consulta = Assert.Single(consultas.Elementos);
        Assert.Equal(("ConsultaHistorialPaciente", medicoId), (consulta.Accion, consulta.UsuarioId));
        Assert.Contains(ficha.Id, consulta.Referencias);
        Assert.Contains(ficha.Id.ToString(), egresos);
        Assert.DoesNotContain("Fallecimiento", egresos);
        Assert.DoesNotContain("\"detalle\"", egresos);
        Assert.Equal(HttpStatusCode.Forbidden, (await enfermera.GetAsync("/api/v1/auditoria")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrador.GetAsync("/api/v1/auditoria?accion=BorrarTodo")).StatusCode);
    }

    [Fact]
    public async Task ElAdministradorNoVeElHistorialNiLasNotasDeUnPaciente()
    {
        var (_, ficha, _, _) = await PacienteConSensorAsync();
        var administrador = await ClienteAdministradorAsync();

        foreach (var ruta in new[] { "hospitalizaciones", "riesgo/historial", "signos/historial", "observaciones", "resumen", "alertas", "notas" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync($"/api/v1/pacientes/{ficha.Id}/{ruta}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.PostAsJsonAsync($"/api/v1/pacientes/{ficha.Id}/notas",
            new { tipo = "Evolucion", texto = "El administrador no escribe notas." })).StatusCode);
    }
}
