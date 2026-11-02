using Vitalify.Application.Alertas;
using Vitalify.Application.Clinica;
using Vitalify.Application.Puertos;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Notificaciones;
using Vitalify.Domain.Telemetria;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Tests.Alertas;

public class AlertasTests
{
    private readonly Escenario _e = new();

    private async Task<(Guid PacienteId, Usuario Enfermera, Usuario Medico)> PacienteConSensorAsync()
    {
        var enfermera = _e.AgregarUsuario("enfermera@hospital.pe", "Segura123", Rol.Enfermera, "Enfermera de turno");
        var medico = _e.AgregarUsuario("medico@hospital.pe", "Segura123", Rol.Medico, "Médico del servicio");
        var ficha = await _e.HaceAsync(TimeSpan.FromMinutes(5), async () =>
        {
            var f = await _e.IngresarAsync();
            await _e.VincularAsync(f.Id, _e.AgregarDispositivo());
            return f;
        });
        return (ficha.Id, enfermera, medico);
    }

    private Task Lectura(int fr, int spo2, int fc, bool caida = false) =>
        _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new SignosMedidos(Fc: fc, Fr: fr, Spo2: spo2), caida: caida));

    [Fact]
    public async Task Una_alerta_alta_notifica_a_la_enfermera_y_al_medico_y_escala_a_los_5_minutos_con_reloj_falso()
    {
        var (_, enfermera, medico) = await PacienteConSensorAsync();
        var otra = _e.AgregarUsuario("otra@hospital.pe", "Segura123", Rol.Enfermera, "Otra enfermera");
        _e.Moviles.Agregar(DispositivoMovil.Registrar(enfermera.Id, "token-enfermera", PlataformaMovil.Android, _e.Reloj.AhoraUtc));

        await Lectura(fr: 25, spo2: 91, fc: 95); // NEWS2 7

        var alerta = Assert.Single(_e.Alertas.Alertas);
        Assert.Equal((TipoAlerta.Riesgo, PrioridadAlerta.Alta), (alerta.Tipo, alerta.Prioridad));
        Assert.Equal(
            new[] { enfermera.Id, otra.Id, medico.Id }.Order(),
            alerta.NotificadaA.Where(n => n.Canal == "tiempo-real").Select(n => n.UsuarioId).Order());
        Assert.Equal("enviado", alerta.NotificadaA.Single(n => n.Canal == "push" && n.UsuarioId == enfermera.Id).Resultado);
        Assert.Equal("sin-dispositivo", alerta.NotificadaA.Single(n => n.Canal == "push" && n.UsuarioId == medico.Id).Resultado);

        _e.Reloj.Avanzar(TimeSpan.FromMinutes(4));
        Assert.Equal(0, (await _e.VigilarAlertas().EjecutarAsync()).Escaladas);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        Assert.Equal(1, (await _e.VigilarAlertas().EjecutarAsync()).Escaladas);
        Assert.Equal(0, (await _e.VigilarAlertas().EjecutarAsync()).Escaladas);
        Assert.NotNull(alerta.EscaladaEn);
    }

    [Fact]
    public async Task Con_programacion_solo_avisa_a_la_enfermera_de_turno()
    {
        var (_, enfermera, _) = await PacienteConSensorAsync();
        var libre = _e.AgregarUsuario("libre@hospital.pe", "Segura123", Rol.Enfermera, "De descanso");
        foreach (var turno in _e.Catalogo.TurnosEn(_e.Reloj.AhoraUtc))
        {
            _e.Programacion.Turnos.Add(Domain.Turnos.ProgramacionTurno.Crear(_e.Servicio.Id, turno, enfermera.Id, "A", Guid.NewGuid(), _e.Reloj.AhoraUtc));
        }

        await Lectura(fr: 16, spo2: 97, fc: 80, caida: true);

        var caida = Assert.Single(_e.Alertas.Alertas);
        Assert.Equal([enfermera.Id], caida.NotificadaA.Where(n => n.Canal == "tiempo-real").Select(n => n.UsuarioId));
        Assert.DoesNotContain(caida.NotificadaA, n => n.UsuarioId == libre.Id);
    }

    [Fact]
    public async Task Un_token_no_registrado_se_borra()
    {
        var (_, enfermera, _) = await PacienteConSensorAsync();
        _e.Moviles.Agregar(DispositivoMovil.Registrar(enfermera.Id, "token-viejo", PlataformaMovil.Android, _e.Reloj.AhoraUtc));
        _e.Push.NoRegistrados.Add("token-viejo");

        await Lectura(fr: 16, spo2: 97, fc: 80, caida: true);

        Assert.Empty(_e.Moviles.Dispositivos);
        Assert.Equal("no-registrado", _e.Alertas.Alertas.Single().NotificadaA.Single(n => n.Canal == "push" && n.UsuarioId == enfermera.Id).Resultado);
    }

    [Fact]
    public async Task Si_el_notificador_falla_la_lectura_igual_se_guarda()
    {
        await PacienteConSensorAsync();
        _e.TiempoRealFalla = true;

        await Lectura(fr: 25, spo2: 91, fc: 95);

        Assert.Single(_e.Historial.Lecturas);
        Assert.Contains(_e.Bitacora.Fallos, f => f.Operacion.StartsWith("Tiempo real", StringComparison.Ordinal));
        Assert.Contains(_e.Alertas.Alertas.Single().NotificadaA, n => n.Canal == "tiempo-real" && n.Resultado == "error");
    }

    [Fact]
    public async Task Una_toma_vencida_crea_toma_pendiente_que_se_resuelve_al_registrar_la_siguiente()
    {
        var (pacienteId, enfermera, _) = await PacienteConSensorAsync();
        var toma = new TomaManualComando { Fr = 16, Pas = 120, Pad = 80, Conciencia = new ConcienciaTomada("Alerta"), OxigenoSuplementario = false };
        await _e.RegistrarObservacion().EjecutarAsync(new RegistrarObservacionComando(pacienteId, toma, enfermera.Id, null));

        _e.Reloj.Avanzar(TimeSpan.FromHours(12).Add(TimeSpan.FromMinutes(29)));
        Assert.Equal(0, (await _e.VigilarAlertas().EjecutarAsync()).TomasPendientes);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(2));
        Assert.Equal(1, (await _e.VigilarAlertas().EjecutarAsync()).TomasPendientes);
        Assert.Equal(PrioridadAlerta.Baja, _e.Alertas.Alertas.Single(a => a.Tipo == TipoAlerta.TomaPendiente).Prioridad);

        await _e.RegistrarObservacion().EjecutarAsync(new RegistrarObservacionComando(pacienteId, toma, enfermera.Id, null));

        Assert.Equal(AlertaClinica.NotaResolucionAutomatica, _e.Alertas.Alertas.Single(a => a.Tipo == TipoAlerta.TomaPendiente).NotaAtencion);
    }

    [Fact]
    public async Task Atender_y_tomar_exigen_el_mismo_servicio()
    {
        var (_, _, _) = await PacienteConSensorAsync();
        await Lectura(fr: 25, spo2: 91, fc: 95);
        var alerta = _e.Alertas.Alertas.Single();
        var ajena = _e.AgregarUsuario("ajena@hospital.pe", "Segura123", Rol.Enfermera, servicioId: Guid.NewGuid());

        var tomar = await _e.TomarAlerta().EjecutarAsync(new TomarAlertaComando(alerta.Id, ajena.Id, null));
        var sinNota = await _e.AtenderAlerta().EjecutarAsync(new AtenderAlertaComando(alerta.Id, "ok", ajena.Id, null));

        Assert.Equal("alerta-de-otro-servicio", tomar.Error.Codigo);
        Assert.Equal("validacion", sinNota.Error.Codigo);
    }
}
