using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Vitalify.Api.Contratos;
using Vitalify.Api.IntegrationTests.Infraestructura;
using Vitalify.Application.Alertas;
using Vitalify.Application.Camas;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Sesiones;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Telemetria;
using static Vitalify.Api.IntegrationTests.Infraestructura.ClienteApi;

namespace Vitalify.Api.IntegrationTests;

/// <summary>Fase 6 · Alertas clínicas, notificaciones (push y SignalR) y escalamiento.</summary>
public class Fase6AlertasTests(FabricaVitalify fabrica) : PruebaApi(fabrica)
{
    /// <summary>Un servicio propio con una enfermera y un médico (cada uno con su token de push) y un paciente con sensor.</summary>
    private sealed record Sala(
        Guid ServicioId, HttpClient Enfermera, SesionIniciada SesionEnfermera, HttpClient Medico, SesionIniciada SesionMedico,
        HttpClient Administrador, FichaPacienteDto Paciente, DispositivoDto Sensor, CamaDto Cama);

    private async Task<Sala> SalaAsync(TimeSpan? vinculadoHace = null)
    {
        var administrador = await ClienteAdministradorAsync();
        var servicio = await administrador.CrearServicioAsync();
        var (enfermera, sesionEnfermera) = await ClienteConRolAsync("Enfermera", servicio.Id);
        var (medico, sesionMedico) = await ClienteConRolAsync("Medico", servicio.Id);
        foreach (var (cliente, sesion) in new[] { (enfermera, sesionEnfermera), (medico, sesionMedico) })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await cliente.PostAsJsonAsync("/api/v1/notificaciones/dispositivos",
                new { token = $"fcm-{sesion.Usuario.Id}", plataforma = "Android" })).StatusCode);
        }

        var cama = await administrador.CrearCamaAsync(servicio.Id);
        var sensor = await administrador.CrearDispositivoAsync();
        var ficha = await enfermera.IngresarPacienteAsync(cama.Id);

        // La ingesta rechaza lo medido antes de la vinculación: se vincula un poco antes para poder enviar "ahora".
        using (Fabrica.Reloj.Adelantar(-(vinculadoHace ?? TimeSpan.FromMinutes(1))))
        {
            Assert.Equal(HttpStatusCode.OK, (await enfermera.VincularAsync(ficha.Id, sensor.Id)).StatusCode);
        }

        return new Sala(servicio.Id, enfermera, sesionEnfermera, medico, sesionMedico, administrador, ficha, sensor, cama);
    }

    /// <summary>Lectura del sensor (endpoint de desarrollo) medida ahora según el reloj de la API.</summary>
    private Task<Application.Telemetria.ResultadoIngesta> EnviarAsync(Sala sala, int fr, int spo2, int fc, bool caida = false, decimal? temp = null) =>
        sala.Administrador.RegistrarTelemetriaAsync(new
        {
            dispositivo = sala.Sensor.Codigo, ts = Ts(Fabrica.Reloj.AhoraUtc), seq = 1, fr, spo2, fc, temp, caida,
        });

    /// <summary>NEWS2 7 con el sensor: FR 25 (3) + SpO2 91 (3) + FC 95 (1).</summary>
    private Task<Application.Telemetria.ResultadoIngesta> News2SieteAsync(Sala sala) => EnviarAsync(sala, fr: 25, spo2: 91, fc: 95);

    /// <summary>NEWS2 5 con el sensor: FR 22 (2) + SpO2 93 (2) + FC 95 (1).</summary>
    private Task<Application.Telemetria.ResultadoIngesta> News2CincoAsync(Sala sala) => EnviarAsync(sala, fr: 22, spo2: 93, fc: 95);

    private Task<AlertaClinica> AlertaAsync(Sala sala, TipoAlerta tipo) =>
        Fabrica.ConsultarAsync(db => db.Alertas.AsNoTracking().SingleAsync(a => a.HospitalizacionId == sala.Paciente.HospitalizacionActiva!.Id && a.Tipo == tipo));

    [Fact]
    public async Task AL_E1_News2SieteCreaUnaAlertaAlta_YNotificaALaEnfermeraDeTurnoYALosMedicos()
    {
        var sala = await SalaAsync();

        await News2SieteAsync(sala);

        var alerta = await AlertaAsync(sala, TipoAlerta.Riesgo);
        Assert.Equal((PrioridadAlerta.Alta, EstadoAlerta.Pendiente, "alto", (int?)7), (alerta.Prioridad, alerta.Estado, alerta.NivelRiesgo, alerta.News2));
        var push = alerta.NotificadaA.Where(n => n.Canal == "push").ToList();
        Assert.Equal(
            new[] { sala.SesionEnfermera.Usuario.Id, sala.SesionMedico.Usuario.Id }.Order(),
            push.Select(n => n.UsuarioId).Order());
        Assert.All(push, n => Assert.Equal(("nueva", "enviado"), (n.Motivo, n.Resultado)));
        Assert.Contains(alerta.NotificadaA, n => n.Canal == "tiempo-real" && n.UsuarioId == sala.SesionEnfermera.Usuario.Id);

        // El push no lleva datos del paciente: solo la cama, el tipo y el puntaje.
        var (tokens, mensaje) = Assert.Single(Fabrica.Push.DeAlerta(alerta.Id));
        Assert.Equal(2, tokens.Count);
        Assert.Equal(($"Vitalify · Cama {sala.Cama.Codigo}", "Riesgo alto (NEWS2 7). Toque para atender"), (mensaje.Titulo, mensaje.Cuerpo));
        Assert.Equal((alerta.Id.ToString(), sala.Paciente.Id.ToString(), "riesgo"),
            (mensaje.Datos["alertaId"], mensaje.Datos["pacienteId"], mensaje.Datos["tipo"]));
        Assert.DoesNotContain(sala.Paciente.NombreCompleto, mensaje.Titulo + mensaje.Cuerpo);

        var lista = (await sala.Enfermera.GetFromJsonAsync<PaginaRespuesta<AlertaDto>>("/api/v1/alertas"))!;
        Assert.Equal(("riesgo", "alta", "pendiente", sala.Cama.Codigo), (lista.Elementos.Single().Tipo, lista.Elementos.Single().Prioridad, lista.Elementos.Single().Estado, lista.Elementos.Single().Cama));
    }

    [Fact]
    public async Task AL_E2_News2SubeDeCincoASiete_SeActualizaLaMismaAlertaYSeVuelveANotificar()
    {
        var sala = await SalaAsync();

        await News2CincoAsync(sala);
        var media = await AlertaAsync(sala, TipoAlerta.Riesgo);
        Assert.Equal((PrioridadAlerta.Media, "medio"), (media.Prioridad, media.NivelRiesgo));

        await EnviarAsync(sala, fr: 22, spo2: 93, fc: 96); // mismo nivel: valores nuevos, sin notificar
        await News2SieteAsync(sala);

        var alta = await AlertaAsync(sala, TipoAlerta.Riesgo);
        Assert.Equal(media.Id, alta.Id);
        Assert.Equal((PrioridadAlerta.Alta, "alto", (int?)7), (alta.Prioridad, alta.NivelRiesgo, alta.News2));
        Assert.Equal(["nueva", "subio"], alta.NotificadaA.Where(n => n.Canal == "push").Select(n => n.Motivo).Distinct());
        Assert.Equal(2, Fabrica.Push.DeAlerta(alta.Id).Count);
    }

    [Fact]
    public async Task AL_E3_UnaAlertaAltaSinTomarEscalaALosCincoMinutos()
    {
        var sala = await SalaAsync();
        var (otraEnfermera, sesionOtra) = await ClienteConRolAsync("Enfermera", sala.ServicioId);
        await News2SieteAsync(sala);

        using (Fabrica.Reloj.Adelantar(TimeSpan.FromMinutes(4)))
        {
            await Fabrica.VigilarAlertasAsync();
        }

        Assert.Null((await AlertaAsync(sala, TipoAlerta.Riesgo)).EscaladaEn);

        using (Fabrica.Reloj.Adelantar(TimeSpan.FromMinutes(6)))
        {
            await Fabrica.VigilarAlertasAsync();
            await Fabrica.VigilarAlertasAsync(); // una sola vez
        }

        var alerta = await AlertaAsync(sala, TipoAlerta.Riesgo);
        Assert.NotNull(alerta.EscaladaEn);
        var escalamiento = alerta.NotificadaA.Where(n => n.Motivo == "escalamiento" && n.Canal == "tiempo-real").Select(n => n.UsuarioId).ToList();
        Assert.Equal(
            new[] { sala.SesionEnfermera.Usuario.Id, sala.SesionMedico.Usuario.Id, sesionOtra.Usuario.Id }.Order(),
            escalamiento.Order());
        Assert.StartsWith("Sin atender:", Fabrica.Push.DeAlerta(alerta.Id).Last().Mensaje.Cuerpo);
        Assert.NotNull(otraEnfermera);
    }

    [Fact]
    public async Task AL_E4_UnaCaidaCreaUnaAlertaAlta()
    {
        var sala = await SalaAsync();

        await EnviarAsync(sala, fr: 16, spo2: 97, fc: 80, caida: true);

        var alerta = await AlertaAsync(sala, TipoAlerta.Caida);
        Assert.Equal((PrioridadAlerta.Alta, sala.Sensor.Codigo), (alerta.Prioridad, alerta.Detalle!["dispositivo"]));
        Assert.Equal("Posible caída del paciente. Toque para atender", Fabrica.Push.DeAlerta(alerta.Id).Single().Mensaje.Cuerpo);
    }

    [Fact]
    public async Task AL_E5_CientoOchentaSegundosSinSenalCreanSinSenal_QueSeCierraSolaAlVolverLaSenal()
    {
        var sala = await SalaAsync(vinculadoHace: TimeSpan.Zero);

        using (Fabrica.Reloj.Adelantar(TimeSpan.FromSeconds(170)))
        {
            await Fabrica.VigilarAlertasAsync();
        }

        Assert.False(await Fabrica.ConsultarAsync(db => db.Alertas.AnyAsync(a => a.HospitalizacionId == sala.Paciente.HospitalizacionActiva!.Id)));

        using (Fabrica.Reloj.Adelantar(TimeSpan.FromSeconds(185)))
        {
            await Fabrica.VigilarAlertasAsync();
            var sinSenal = await AlertaAsync(sala, TipoAlerta.SinSenal);
            Assert.Equal((PrioridadAlerta.Media, EstadoAlerta.Pendiente, "sin-lecturas"), (sinSenal.Prioridad, sinSenal.Estado, sinSenal.Detalle!["motivo"]));

            await EnviarAsync(sala, fr: 16, spo2: 97, fc: 80);
        }

        var resuelta = await AlertaAsync(sala, TipoAlerta.SinSenal);
        Assert.Equal((EstadoAlerta.Atendida, AlertaClinica.NotaResolucionAutomatica, (Guid?)null), (resuelta.Estado, resuelta.NotaAtencion, resuelta.AtendidaPor));
    }

    [Fact]
    public async Task AL_E6_UnaTomaManualVencidaCreaTomaPendiente_QueSeCierraSolaAlRegistrarLaToma()
    {
        var sala = await SalaAsync();
        // Toma con todo normal (NEWS2 0): próxima a las 12 h; vale 12 h (con la tolerancia, el tope).
        await sala.Enfermera.PostAsJsonAsync($"/api/v1/pacientes/{sala.Paciente.Id}/observaciones",
            new { fr = 16, pas = 120, pad = 80, conciencia = "Alerta", oxigenoSuplementario = false });

        using (Fabrica.Reloj.Adelantar(TimeSpan.FromHours(12).Add(TimeSpan.FromMinutes(31))))
        {
            await Fabrica.VigilarAlertasAsync();
            var pendiente = await AlertaAsync(sala, TipoAlerta.TomaPendiente);
            Assert.Equal((PrioridadAlerta.Baja, EstadoAlerta.Pendiente), (pendiente.Prioridad, pendiente.Estado));

            Assert.Equal(HttpStatusCode.Created, (await sala.Enfermera.PostAsJsonAsync($"/api/v1/pacientes/{sala.Paciente.Id}/observaciones",
                new { pas = 122, pad = 78, conciencia = "Alerta", oxigenoSuplementario = false })).StatusCode);
        }

        var resuelta = await AlertaAsync(sala, TipoAlerta.TomaPendiente);
        Assert.Equal((EstadoAlerta.Atendida, AlertaClinica.NotaResolucionAutomatica), (resuelta.Estado, resuelta.NotaAtencion));
    }

    [Fact]
    public async Task AL_E7_AtenderSinNotaDa400_YConNotaQuedaAtendidaYAuditada()
    {
        var sala = await SalaAsync();
        await News2SieteAsync(sala);
        var alerta = await AlertaAsync(sala, TipoAlerta.Riesgo);

        var sinNota = await sala.Enfermera.PostAsJsonAsync($"/api/v1/alertas/{alerta.Id}/atender", new { nota = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, sinNota.StatusCode);
        Assert.Contains("nota", (await sinNota.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Keys);

        var tomada = (await (await sala.Enfermera.PostAsync($"/api/v1/alertas/{alerta.Id}/tomar", null)).Content.ReadFromJsonAsync<AlertaDto>())!;
        Assert.Equal("en-atencion", tomada.Estado);
        Assert.Equal(HttpStatusCode.Conflict, (await sala.Medico.PostAsync($"/api/v1/alertas/{alerta.Id}/tomar", null)).StatusCode);

        var atendida = await sala.Enfermera.PostAsJsonAsync($"/api/v1/alertas/{alerta.Id}/atender", new { nota = "Se avisó al médico y se colocó oxígeno." });
        Assert.Equal(HttpStatusCode.OK, atendida.StatusCode);
        var dto = (await atendida.Content.ReadFromJsonAsync<AlertaDto>())!;
        Assert.Equal("atendida", dto.Estado);
        Assert.NotNull(dto.SegundosHastaAtendida);

        var historial = (await sala.Medico.GetFromJsonAsync<List<AlertaDto>>($"/api/v1/pacientes/{sala.Paciente.Id}/alertas"))!;
        Assert.Contains(historial, a => a.Id == alerta.Id && a.NotaAtencion == "Se avisó al médico y se colocó oxígeno.");
        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a => a.Accion == AccionAuditoria.AlertaTomada && a.Detalle!.Contains(alerta.Id.ToString()))));
        Assert.True(await Fabrica.ConsultarAsync(db => db.Auditoria.AnyAsync(a => a.Accion == AccionAuditoria.AlertaAtendida && a.Detalle!.Contains(alerta.Id.ToString()))));

        // Una alerta de riesgo no se cierra sola: una lectura normal no la toca y una nueva de riesgo abre otra.
        await EnviarAsync(sala, fr: 16, spo2: 97, fc: 80);
        Assert.Equal(EstadoAlerta.Atendida, (await Fabrica.ConsultarAsync(db => db.Alertas.SingleAsync(a => a.Id == alerta.Id))).Estado);
    }

    [Fact]
    public async Task AL_E8_UnaEnfermeraDeOtroServicioRecibe403()
    {
        var sala = await SalaAsync();
        await News2SieteAsync(sala);
        var alerta = await AlertaAsync(sala, TipoAlerta.Riesgo);
        var otroServicio = await sala.Administrador.CrearServicioAsync("Otro servicio");
        var (ajena, _) = await ClienteConRolAsync("Enfermera", otroServicio.Id);

        foreach (var respuesta in new[]
                 {
                     await ajena.PostAsync($"/api/v1/alertas/{alerta.Id}/tomar", null),
                     await ajena.PostAsJsonAsync($"/api/v1/alertas/{alerta.Id}/atender", new { nota = "Intento de otro servicio" }),
                     await ajena.GetAsync($"/api/v1/alertas?servicioId={sala.ServicioId}"),
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.Equal("alerta-de-otro-servicio", (await respuesta.LeerProblemaAsync()).Type);
        }

        Assert.Empty((await ajena.GetFromJsonAsync<PaginaRespuesta<AlertaDto>>("/api/v1/alertas"))!.Elementos);
        Assert.Equal(HttpStatusCode.Forbidden, (await sala.Administrador.GetAsync("/api/v1/alertas")).StatusCode);
    }

    [Fact]
    public async Task LaPreocupacionClinicaAvisaALosMedicosDelServicio()
    {
        var sala = await SalaAsync();

        await sala.Enfermera.PostAsJsonAsync($"/api/v1/pacientes/{sala.Paciente.Id}/observaciones", new { preocupacionClinica = true, fr = 16 });

        var alerta = await AlertaAsync(sala, TipoAlerta.PreocupacionClinica);
        Assert.Equal(PrioridadAlerta.Media, alerta.Prioridad);
        Assert.Equal([sala.SesionMedico.Usuario.Id], alerta.NotificadaA.Where(n => n.Canal == "push").Select(n => n.UsuarioId));
    }

    [Fact]
    public async Task MonitoreadosMuestraLasAlertasAbiertas()
    {
        var sala = await SalaAsync();
        await News2SieteAsync(sala);
        await EnviarAsync(sala, fr: 25, spo2: 91, fc: 95, caida: true);

        var fila = (await sala.Enfermera.GetFromJsonAsync<PacientesMonitoreadosRespuesta>("/api/v1/pacientes/monitoreados"))!
            .Items.Single(p => p.PacienteId == sala.Paciente.Id);

        Assert.Equal(new AlertasAbiertasDto(2, "alta"), fila.AlertasAbiertas);
    }

    [Fact]
    public async Task UnTokenQueFcmDaPorNoRegistradoSeBorra_YQuitarloAlCerrarSesionLoElimina()
    {
        var sala = await SalaAsync();
        var token = $"fcm-{sala.SesionEnfermera.Usuario.Id}";
        Assert.True(await Fabrica.ConsultarAsync(db => db.DispositivosMoviles.AnyAsync(d => d.UsuarioId == sala.SesionEnfermera.Usuario.Id)));

        var quitar = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/notificaciones/dispositivos") { Content = JsonContent.Create(new { token }) };
        Assert.Equal(HttpStatusCode.NoContent, (await sala.Enfermera.SendAsync(quitar)).StatusCode);

        Assert.False(await Fabrica.ConsultarAsync(db => db.DispositivosMoviles.AnyAsync(d => d.UsuarioId == sala.SesionEnfermera.Usuario.Id)));
        Assert.Equal(HttpStatusCode.BadRequest, (await sala.Enfermera.PostAsJsonAsync("/api/v1/notificaciones/dispositivos", new { token, plataforma = "Nokia" })).StatusCode);
    }

    [Fact]
    public async Task SignalR_LaEnfermeraConectadaAlHubRecibeAlertaCreada()
    {
        var sala = await SalaAsync();
        var recibida = new TaskCompletionSource<AlertaEventoDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var riesgo = new TaskCompletionSource<RiesgoActualizadoDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var conexion = new HubConnectionBuilder()
            .WithUrl(new Uri(Fabrica.Server.BaseAddress, "/hubs/monitoreo"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => Fabrica.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(sala.SesionEnfermera.AccessToken);
            })
            .Build();
        conexion.On<AlertaEventoDto>("alertaCreada", a => recibida.TrySetResult(a));
        conexion.On<RiesgoActualizadoDto>("riesgoActualizado", r => riesgo.TrySetResult(r));
        await conexion.StartAsync();

        await News2SieteAsync(sala);

        var evento = await recibida.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(("riesgo", "alta", sala.Paciente.Id, sala.Cama.Codigo, "nueva"), (evento.Tipo, evento.Prioridad, evento.PacienteId, evento.Cama, evento.Motivo));
        Assert.Contains(sala.SesionEnfermera.Usuario.Id, evento.Destinatarios);
        Assert.Equal((7, "alto"), ((await riesgo.Task.WaitAsync(TimeSpan.FromSeconds(15))).News2, (await riesgo.Task).Nivel));

        // La notificación registra que la enfermera tenía una conexión abierta.
        var alerta = await AlertaAsync(sala, TipoAlerta.Riesgo);
        Assert.Contains(alerta.NotificadaA, n => n.Canal == "tiempo-real" && n.UsuarioId == sala.SesionEnfermera.Usuario.Id && n.Resultado == "conectado");
    }

    [Fact]
    public async Task SignalR_SinTokenOConElTokenDelAdministradorNoSePuedeConectar()
    {
        var administrador = Fabrica.CrearCliente();
        var sesion = await administrador.IniciarSesionAsync(FabricaVitalify.AdminCorreo, FabricaVitalify.AdminContrasena);

        foreach (var token in new[] { null, sesion.AccessToken })
        {
            await using var conexion = new HubConnectionBuilder()
                .WithUrl(new Uri(Fabrica.Server.BaseAddress, "/hubs/monitoreo"), o =>
                {
                    o.Transports = HttpTransportType.LongPolling;
                    o.HttpMessageHandlerFactory = _ => Fabrica.Server.CreateHandler();
                    o.AccessTokenProvider = () => Task.FromResult(token);
                })
                .Build();
            await Assert.ThrowsAnyAsync<Exception>(() => conexion.StartAsync());
        }
    }

    [Fact]
    public async Task Regla_0_1_UnaLecturaAnteriorALaVinculacionSeRechazaConAnteriorAVinculacion()
    {
        var sala = await SalaAsync(vinculadoHace: TimeSpan.Zero);
        var antes = Ms(Fabrica.Reloj.AhoraUtc.AddMinutes(-10));

        var resultado = await sala.Administrador.RegistrarTelemetriaAsync(new { dispositivo = sala.Sensor.Codigo, ts = Ts(antes), seq = 7, fc = 130, spo2 = 88 });

        Assert.Equal("Rechazada", resultado.Estado);
        var incidencia = Assert.Single(resultado.Incidencias);
        Assert.Equal(("AnteriorAVinculacion", sala.Paciente.HospitalizacionActiva!.Id), (incidencia.Tipo, incidencia.HospitalizacionId));
        Assert.False(await Fabrica.ConsultarHistorialAsync(db => db.Lecturas.AnyAsync(l => l.CodigoDispositivo == sala.Sensor.Codigo)));
        Assert.False(await Fabrica.ConsultarHistorialAsync(db => db.EstadosSignos.AnyAsync(e => e.HospitalizacionId == sala.Paciente.HospitalizacionActiva!.Id)));
        Assert.False(await Fabrica.ConsultarAsync(db => db.Alertas.AnyAsync(a => a.HospitalizacionId == sala.Paciente.HospitalizacionActiva!.Id)));
    }
}
