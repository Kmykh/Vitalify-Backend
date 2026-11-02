using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Turnos;
using Vitalify.Domain.Auditoria;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>Fase 6 · Servicios, programación mensual de turnos (formato de la planilla) y entrega de turno.</summary>
public class Fase6TurnosTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    private static readonly string[] Letras = ["A", "B", "C", "D", "E"];

    /// <summary>Instante en UTC de una hora de Lima (UTC−5, sin horario de verano).</summary>
    private static DateTime Lima(int anio, int mes, int dia, int hora) => new DateTime(anio, mes, dia, hora, 0, 0, DateTimeKind.Utc).AddHours(5);

    /// <summary>Un servicio con cinco enfermeras (letras A a E).</summary>
    private async Task<(Guid ServicioId, HttpClient Administrador, Dictionary<string, (HttpClient Cliente, SesionIniciada Sesion)> Enfermeras)> ServicioConCincoAsync()
    {
        var administrador = await ClienteAdministradorAsync();
        var servicio = await administrador.CrearServicioAsync("Emergencia de pruebas");
        var enfermeras = new Dictionary<string, (HttpClient, SesionIniciada)>();
        foreach (var letra in Letras)
        {
            enfermeras[letra] = await ClienteConRolAsync("Enfermera", servicio.Id);
        }

        return (servicio.Id, administrador, enfermeras);
    }

    /// <summary>Planilla rotativa de 30 días: diurna = letra (día % 5), nocturna = letra ((día + 2) % 5). Nadie tiene turnos que se crucen.</summary>
    private static object Planilla(Dictionary<string, (HttpClient Cliente, SesionIniciada Sesion)> enfermeras) => new
    {
        letras = enfermeras.ToDictionary(e => e.Key, e => e.Value.Sesion.Usuario.Id),
        dias = Enumerable.Range(1, 30).Select(d => new Dictionary<string, object>
        {
            ["dia"] = d,
            ["GuardiaDiurna"] = new[] { Letras[d % 5] },
            ["GuardiaNocturna"] = new[] { Letras[(d + 2) % 5] },
        }).ToList(),
    };

    [Fact]
    public async Task TU_E1_CargarLaPlanillaDeEjemplo_YTurnosActualDevuelveLaLetraCorrectaALas3YALas15()
    {
        var (servicioId, administrador, enfermeras) = await ServicioConCincoAsync();

        var carga = await administrador.PutAsJsonAsync($"/api/v1/servicios/{servicioId}/programacion/2026/11", Planilla(enfermeras));

        Assert.Equal(HttpStatusCode.OK, carga.StatusCode);
        var planilla = (await carga.Content.ReadFromJsonAsync<ProgramacionMesDto>())!;
        Assert.Equal((60, 5, 30), (planilla.Turnos, planilla.Letras.Count, planilla.Dias.Count));
        Assert.Equal(["D"], planilla.Dias.Single(d => d.Dia == 1).Turnos["GuardiaNocturna"]); // (1 + 2) % 5 = 3 → D
        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a => a.Accion == AccionAuditoria.ProgramacionCargada && a.Detalle!.Contains(servicioId.ToString()))));

        var consultor = enfermeras["A"].Cliente;
        // 03:00 del día 10 → nocturna que empezó el día 9: letra (9 + 2) % 5 = 1 → B.
        using (Fabrica.Reloj.Adelantar(Lima(2026, 11, 10, 3) - DateTime.UtcNow))
        {
            var actual = (await consultor.GetFromJsonAsync<TurnoActualDto>($"/api/v1/turnos/actual?servicioId={servicioId}"))!;
            var deTurno = Assert.Single(actual.Enfermeras);
            Assert.Equal(("B", "GuardiaNocturna", enfermeras["B"].Sesion.Usuario.Id, false), (deTurno.Letra, deTurno.Turno, deTurno.UsuarioId, actual.EsRespaldo));
        }

        // 15:00 del día 10 → diurna del día 10: letra 10 % 5 = 0 → A.
        using (Fabrica.Reloj.Adelantar(Lima(2026, 11, 10, 15) - DateTime.UtcNow))
        {
            var actual = (await consultor.GetFromJsonAsync<TurnoActualDto>("/api/v1/turnos/actual"))!;
            Assert.Equal(("A", "GuardiaDiurna"), (actual.Enfermeras.Single().Letra, actual.Enfermeras.Single().Turno));
        }

        var mios = (await consultor.GetFromJsonAsync<List<MiTurnoDto>>("/api/v1/turnos/mios?desde=2026-11-01&hasta=2026-11-30"))!;
        Assert.Equal(12, mios.Count); // 6 diurnas y 6 nocturnas
        Assert.All(mios, t => Assert.Equal("A", t.Letra));

        // La planilla guardada se lee igual que se cargó.
        var leida = (await administrador.GetFromJsonAsync<ProgramacionMesDto>($"/api/v1/servicios/{servicioId}/programacion/2026/11"))!;
        Assert.Equal(planilla.Dias.Single(d => d.Dia == 17).Turnos["GuardiaDiurna"], leida.Dias.Single(d => d.Dia == 17).Turnos["GuardiaDiurna"]);
    }

    [Fact]
    public async Task TU_E2_DosTurnosQueSeCruzanDan400PorDiaYLetra()
    {
        var (servicioId, administrador, enfermeras) = await ServicioConCincoAsync();

        var respuesta = await administrador.PutAsJsonAsync($"/api/v1/servicios/{servicioId}/programacion/2026/12", new
        {
            letras = enfermeras.ToDictionary(e => e.Key, e => e.Value.Sesion.Usuario.Id),
            dias = new object[]
            {
                new Dictionary<string, object> { ["dia"] = 3, ["GuardiaDiurna"] = new[] { "D" }, ["GuardiaNocturna"] = new[] { "B" }, ["Tarde"] = new[] { "D" } },
                new Dictionary<string, object> { ["dia"] = 32, ["GuardiaDiurna"] = new[] { "A" } },
                new Dictionary<string, object> { ["dia"] = 4, ["GuardiaDiurna"] = new[] { "Z" } },
            },
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        var errores = (await respuesta.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors;
        Assert.Contains("se cruzan", errores["dias.3.D"].Single());
        Assert.Contains("dias.32", errores.Keys);
        Assert.Contains("dias.4.Z", errores.Keys);

        // Una letra que no es una enfermera del servicio.
        var (ajena, sesionAjena) = await ClienteConRolAsync("Enfermera");
        var otraLetra = await administrador.PutAsJsonAsync($"/api/v1/servicios/{servicioId}/programacion/2026/12", new
        {
            letras = new Dictionary<string, Guid> { ["A"] = sesionAjena.Usuario.Id },
            dias = new[] { new Dictionary<string, object> { ["dia"] = 1, ["GuardiaDiurna"] = new[] { "A" } } },
        });
        Assert.Contains("letras.A", (await otraLetra.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);
        Assert.NotNull(ajena);
        Assert.Equal(HttpStatusCode.Forbidden, (await enfermeras["A"].Cliente.PutAsJsonAsync($"/api/v1/servicios/{servicioId}/programacion/2026/12", new { })).StatusCode);
    }

    [Fact]
    public async Task TU_E3_SinProgramacion_SeUsaElRespaldoConLasEnfermerasActivasDelServicio()
    {
        var (servicioId, _, enfermeras) = await ServicioConCincoAsync();

        var actual = (await enfermeras["C"].Cliente.GetFromJsonAsync<TurnoActualDto>($"/api/v1/turnos/actual?servicioId={servicioId}"))!;

        Assert.True(actual.EsRespaldo);
        Assert.Equal(enfermeras.Values.Select(e => e.Sesion.Usuario.Id).Order(), actual.Enfermeras.Select(e => e.UsuarioId).Order());
        Assert.All(actual.Enfermeras, e => Assert.Null(e.Letra));
    }

    [Fact]
    public async Task LaEntregaDeTurnoMuestraAlPacienteEnRiesgoPrimero_ConTendenciaAlertasYProximaToma()
    {
        var administrador = await ClienteAdministradorAsync();
        var servicio = await administrador.CrearServicioAsync("UVI de pruebas");
        var (enfermera, _) = await ClienteConRolAsync("Enfermera", servicio.Id);
        var estable = await enfermera.IngresarPacienteAsync((await administrador.CrearCamaAsync(servicio.Id)).Id);
        var grave = await enfermera.IngresarPacienteAsync((await administrador.CrearCamaAsync(servicio.Id)).Id);
        var sensor = await administrador.CrearDispositivoAsync();
        using (Fabrica.Reloj.Adelantar(TimeSpan.FromMinutes(-2)))
        {
            Assert.Equal(HttpStatusCode.OK, (await enfermera.VincularAsync(grave.Id, sensor.Id)).StatusCode);
        }

        await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{estable.Id}/observaciones", new { fr = 16, pas = 120, pad = 80, conciencia = "Alerta", oxigenoSuplementario = false });
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc.AddSeconds(-30)), seq = 1, fr = 22, spo2 = 93, fc = 95 });
        await administrador.RegistrarTelemetriaAsync(new { dispositivo = sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 2, fr = 25, spo2 = 91, fc = 95 });
        await enfermera.PostAsJsonAsync($"/api/v1/pacientes/{grave.Id}/observaciones", new { preocupacionClinica = true });

        var entrega = (await enfermera.GetFromJsonAsync<EntregaTurnoDto>($"/api/v1/servicios/{servicio.Id}/entrega-turno"))!;

        Assert.Equal([grave.Id, estable.Id], entrega.Pacientes.Select(p => p.PacienteId));
        var enRiesgo = entrega.Pacientes[0];
        Assert.Equal("alto", enRiesgo.NivelRiesgo);
        Assert.Equal((5, 7), (enRiesgo.News2.Primero!.Value, enRiesgo.News2.Maximo!.Value));
        Assert.Contains(enRiesgo.AlertasAbiertas, a => a.Tipo == "riesgo" && a.Prioridad == "alta");
        Assert.Contains(enRiesgo.AlertasAbiertas, a => a.Tipo == "preocupacion-clinica");
        Assert.Equal(("con-datos", sensor.Codigo), (enRiesgo.Sensor!.Senal, enRiesgo.Sensor.Codigo));
        Assert.NotNull(enRiesgo.UltimaPreocupacionClinica);
        Assert.NotNull(entrega.Pacientes[1].ProximaTomaManualEn);
        Assert.False(entrega.Pacientes[1].TomaVencida);

        var (ajena, _) = await ClienteConRolAsync("Medico");
        Assert.Equal(HttpStatusCode.Forbidden, (await ajena.GetAsync($"/api/v1/servicios/{servicio.Id}/entrega-turno")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync($"/api/v1/servicios/{servicio.Id}/entrega-turno")).StatusCode);
    }

    [Fact]
    public async Task ElAdministradorNoVeAlertasNiLaEntregaDeTurno_PeroSiLaProgramacion()
    {
        var administrador = await ClienteAdministradorAsync();
        var servicio = await administrador.CrearServicioAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync("/api/v1/alertas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync($"/api/v1/servicios/{servicio.Id}/entrega-turno")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.GetAsync($"/api/v1/turnos/actual?servicioId={servicio.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrador.PostAsJsonAsync("/api/v1/notificaciones/dispositivos", new { token = "t", plataforma = "Android" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await administrador.GetAsync($"/api/v1/servicios/{servicio.Id}/programacion/2026/11")).StatusCode);
    }

    [Fact]
    public async Task ElAdministradorAsignaElServicioDeUnaEnfermeraYCreaServicios()
    {
        var administrador = await ClienteAdministradorAsync();
        var servicio = await administrador.CrearServicioAsync();
        var (enfermera, sesion) = await ClienteConRolAsync("Enfermera");

        var asignado = await administrador.PutAsJsonAsync($"/api/v1/usuarios/{sesion.Usuario.Id}/servicio", new { servicioId = servicio.Id });

        Assert.Equal(HttpStatusCode.OK, asignado.StatusCode);
        Assert.Equal(servicio.Id, (await asignado.Content.ReadFromJsonAsync<Application.Usuarios.UsuarioDetalleDto>())!.ServicioId);
        Assert.Equal(HttpStatusCode.Forbidden, (await enfermera.PostAsJsonAsync("/api/v1/servicios", new { codigo = "X-NO", nombre = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrador.PostAsJsonAsync("/api/v1/servicios", new { codigo = servicio.Codigo, nombre = "Repetido" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrador.PostAsJsonAsync("/api/v1/usuarios",
            new { nombre = "Sin servicio", correo = $"s.{Guid.NewGuid():N}@vitalify.test", contrasena = ContrasenaDePrueba, rol = "Medico" })).StatusCode);
    }
}
