using Vitalify.Application.Alertas;
using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Notas;
using Vitalify.Application.Tests.Fakes;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Telemetria;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Application.Tests.Historial;

public class HistorialTests
{
    private readonly Escenario _e = new();

    /// <summary>Paciente ingresado y vinculado hace <paramref name="hace"/>, y una enfermera y un médico del servicio.</summary>
    private async Task<(Guid PacienteId, Usuario Enfermera, Usuario Medico)> PacienteAsync(TimeSpan? hace = null)
    {
        var enfermera = _e.AgregarUsuario("enfermera@hospital.pe", "Segura123", Rol.Enfermera, "Rosa Enfermera");
        var medico = _e.AgregarUsuario("medico@hospital.pe", "Segura123", Rol.Medico, "Luis Médico");
        var ficha = await _e.HaceAsync(hace ?? TimeSpan.FromHours(2), async () =>
        {
            var f = await _e.IngresarAsync();
            await _e.VincularAsync(f.Id, _e.AgregarDispositivo());
            return f;
        });
        return (ficha.Id, enfermera, medico);
    }

    private Task Lectura(int fr, int spo2, int fc, decimal? temp = null, bool caida = false, int segundos = 0) =>
        _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new SignosMedidos(Fc: fc, Fr: fr, Spo2: spo2, Temperatura: temp), segundos: segundos, caida: caida));

    private Task Observar(Guid pacienteId, Guid usuarioId, int fr = 18, int pas = 120, int pad = 80) =>
        _e.RegistrarObservacion().EjecutarAsync(new(pacienteId,
            new TomaManualComando { Fr = fr, Pas = pas, Pad = pad, Conciencia = new ConcienciaTomada("Alerta"), OxigenoSuplementario = false },
            usuarioId, null));

    private int ConsultasAuditadas() => _e.Auditoria.De(AccionAuditoria.ConsultaHistorialPaciente).Count();

    [Fact]
    public async Task Los_signos_agrupados_dan_promedio_minimo_maximo_y_caida_por_intervalo()
    {
        var (pacienteId, enfermera, _) = await PacienteAsync();
        var inicio = new DateTime(_e.Reloj.AhoraUtc.Ticks - (_e.Reloj.AhoraUtc.Ticks % TimeSpan.FromMinutes(5).Ticks), DateTimeKind.Utc);
        _e.Reloj.Avanzar(inicio + TimeSpan.FromMinutes(6) - _e.Reloj.AhoraUtc);
        await Lectura(fr: 16, spo2: 97, fc: 80, segundos: -6 * 60);      // grupo [inicio, +5m)
        await Lectura(fr: 18, spo2: 95, fc: 85, segundos: -5 * 60 - 30); // mismo grupo
        await Lectura(fr: 20, spo2: 93, fc: 90, caida: true);            // grupo siguiente

        var historial = (await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId, Agrupar: "5m", UsuarioId: enfermera.Id))).Valor;

        Assert.Empty(historial.Lecturas);
        Assert.Equal(2, historial.Grupos!.Count);
        var primero = historial.Grupos[0];
        Assert.Equal((inicio, inicio.AddMinutes(5), 2), (primero.Inicio, primero.Fin, primero.Lecturas));
        Assert.Equal((82.5m, 80m, 85m), (primero.Fc!.Promedio, primero.Fc.Minimo, primero.Fc.Maximo));
        Assert.Null(primero.Temperatura);
        Assert.Equal((false, true), (primero.Caida, historial.Grupos[1].Caida));
        Assert.Equal(1, ConsultasAuditadas());
    }

    [Fact]
    public async Task El_historial_de_riesgo_trae_puntos_y_faltantes_y_agrupado_el_peor_nivel()
    {
        var (pacienteId, enfermera, _) = await PacienteAsync();
        await Lectura(fr: 16, spo2: 97, fc: 80, segundos: -60); // NEWS2 0
        await Lectura(fr: 25, spo2: 91, fc: 95);                // NEWS2 7

        var crudo = (await _e.ObtenerHistorialRiesgo().EjecutarAsync(new(pacienteId, null, null, null, null, enfermera.Id, null))).Valor;
        var agrupado = (await _e.ObtenerHistorialRiesgo().EjecutarAsync(new(pacienteId, null, null, null, "1h", enfermera.Id, null))).Valor;

        Assert.Equal([0, 7], crudo.Evaluaciones.Select(e => e.News2.Total));
        Assert.False(crudo.Evaluaciones[1].News2.Completo);
        Assert.Contains("pas", crudo.Evaluaciones[1].News2.Faltantes);
        Assert.Equal(3, crudo.Evaluaciones[1].News2.Puntos["fr"]);
        var grupos = agrupado.Grupos!;
        var news2 = grupos.Count == 1 ? grupos[0].News2 : grupos[^1].News2;
        Assert.Equal((7, "alto"), (news2.Maximo, news2.NivelMaximo));
        Assert.Equal(2, grupos.Sum(g => g.Evaluaciones));
        Assert.Equal(0, grupos.Sum(g => g.Completas));
    }

    [Fact]
    public async Task El_riesgo_no_admite_agrupar_por_minuto()
    {
        var (pacienteId, enfermera, _) = await PacienteAsync();

        var resultado = await _e.ObtenerHistorialRiesgo().EjecutarAsync(new(pacienteId, null, null, null, "1m", enfermera.Id, null));

        Assert.Contains("agrupar", resultado.Error.Detalles.Keys);
    }

    [Fact]
    public async Task Las_observaciones_traen_el_nombre_del_profesional_y_la_mas_reciente_primero()
    {
        var (pacienteId, enfermera, medico) = await PacienteAsync();
        await Observar(pacienteId, enfermera.Id, fr: 18);
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(30));
        await Observar(pacienteId, medico.Id, fr: 22);

        var observaciones = (await _e.ListarObservaciones().EjecutarAsync(new(pacienteId, null, null, null, medico.Id, null))).Valor.Observaciones;

        Assert.Equal([("Luis Médico", 22), ("Rosa Enfermera", 18)], observaciones.Select(o => (o.RegistradaPor.Nombre, o.Fr!.Value)));
        Assert.Equal(("Medico", "Alerta", 120), (observaciones[0].RegistradaPor.Rol, observaciones[0].Conciencia, observaciones[0].Presion.Pas!.Value));
    }

    [Fact]
    public async Task Los_episodios_traen_cama_servicio_motivo_de_egreso_y_sensores_usados()
    {
        var (pacienteId, enfermera, _) = await PacienteAsync();
        await _e.RegistrarEgreso().EjecutarAsync(new(pacienteId, "Traslado", "A UCI", _e.EnfermeraId, null));
        _e.Reloj.Avanzar(TimeSpan.FromDays(3));
        await _e.IngresarAsync("MED-B-02");

        var episodios = (await _e.ListarHospitalizaciones().EjecutarAsync(new(pacienteId, enfermera.Id, null))).Valor.Episodios;

        Assert.Equal(2, episodios.Count);
        Assert.Equal(("Activa", "MED-B-02", 0), (episodios[0].Estado, episodios[0].Cama.Codigo, episodios[0].Sensores.Count));
        var anterior = episodios[1];
        Assert.Equal(("Finalizada", "Traslado", "MED-B-01", "MEDICINA-B"), (anterior.Estado, anterior.MotivoEgreso, anterior.Cama.Codigo, anterior.Servicio.Codigo));
        var sensor = Assert.Single(anterior.Sensores);
        Assert.Equal(("ESP32-001", "Egreso"), (sensor.Codigo, sensor.MotivoLiberacion));
        Assert.NotNull(sensor.LiberadoEn);
    }

    [Fact]
    public async Task El_resumen_junta_extremos_alertas_por_tipo_notas_y_proxima_toma()
    {
        var (pacienteId, enfermera, medico) = await PacienteAsync();
        await Lectura(fr: 16, spo2: 97, fc: 70, temp: 36.1m, segundos: -120);
        await Lectura(fr: 25, spo2: 91, fc: 95, temp: 36.5m, segundos: -60); // NEWS2 7: alerta alta
        await Lectura(fr: 16, spo2: 97, fc: 80, caida: true);                  // alerta de caída
        await Observar(pacienteId, enfermera.Id, pas: 140, pad: 90);
        var caida = _e.Alertas.Alertas.Single(a => a.Tipo == TipoAlerta.Caida);
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(30));
        await _e.TomarAlerta().EjecutarAsync(new(caida.Id, enfermera.Id, null));
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(30));
        await _e.AtenderAlerta().EjecutarAsync(new(caida.Id, "Paciente en el piso, sin lesiones.", enfermera.Id, null));
        for (var i = 1; i <= 6; i++)
        {
            await _e.RegistrarNota().EjecutarAsync(new(pacienteId, "Evolucion", $"Nota de evolución número {i}.", medico.Id, null));
            _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        }

        var resumen = (await _e.ObtenerResumen().EjecutarAsync(new(pacienteId, null, medico.Id, null))).Valor;

        Assert.Equal(("Activa", pacienteId), (resumen.Episodio.Estado, resumen.Paciente.Id));
        Assert.Equal((70m, 95m, 3), (resumen.Extremos.Sensor.Fc!.Minimo, resumen.Extremos.Sensor.Fc.Maximo, resumen.Extremos.Sensor.Fc.Mediciones));
        Assert.Equal((140m, 140m), (resumen.Extremos.TomaManual.Pas!.Minimo, resumen.Extremos.TomaManual.Pas.Maximo));
        Assert.Null(resumen.Extremos.TomaManual.Spo2);
        var porTipo = resumen.Alertas.ToDictionary(a => a.Tipo);
        Assert.Equal(5, porTipo.Count);
        Assert.Equal((1, 0, 30d, 60d), (porTipo["caida"].Cantidad, porTipo["caida"].Abiertas, porTipo["caida"].SegundosMedioHastaTomada, porTipo["caida"].SegundosMedioHastaAtendida));
        Assert.Equal((1, 1), (porTipo["riesgo"].Cantidad, porTipo["riesgo"].Abiertas));
        Assert.Equal("riesgo", Assert.Single(resumen.AlertasAbiertas).Tipo);
        Assert.Equal(NotasEsperadas(6, 2), resumen.UltimasNotas.Select(n => n.Texto));
        Assert.Equal("Luis Médico", resumen.UltimasNotas[0].Autor.Nombre);
        Assert.NotNull(resumen.UltimoRiesgo);
        Assert.Equal(resumen.UltimoRiesgo!.ProximaTomaManualEn, resumen.ProximaTomaManualEn);
        Assert.Equal(1, ConsultasAuditadas());
    }

    private static List<string> NotasEsperadas(int desde, int hasta) =>
        Enumerable.Range(hasta, desde - hasta + 1).Reverse().Select(i => $"Nota de evolución número {i}.").ToList();

    [Fact]
    public async Task Las_alertas_del_paciente_las_lee_otro_servicio_y_quedan_auditadas()
    {
        var (pacienteId, _, _) = await PacienteAsync();
        await Lectura(fr: 25, spo2: 91, fc: 95);
        var otroServicio = Domain.Servicios.Servicio.Registrar("CIRUGIA", "Cirugía");
        _e.Servicios.Servicios.Add(otroServicio);
        var ajeno = _e.AgregarUsuario("ajeno@hospital.pe", "Segura123", Rol.Medico, "Médico de cirugía", otroServicio.Id);

        var alertas = await _e.ListarAlertasPaciente().EjecutarAsync(new(pacienteId, null, ajeno.Id, null));

        Assert.Equal("riesgo", Assert.Single(alertas.Valor).Tipo);
        var registro = Assert.Single(_e.Auditoria.De(AccionAuditoria.ConsultaHistorialPaciente));
        Assert.Equal(ajeno.Id, registro.UsuarioId);
        Assert.Contains(pacienteId.ToString(), registro.Detalle);
    }

    [Fact]
    public async Task Una_hospitalizacion_de_otro_paciente_da_404()
    {
        var (pacienteId, enfermera, _) = await PacienteAsync();
        var otro = await _e.IngresarAsync("MED-B-02", "87654321");

        var resultado = await _e.ObtenerHistorialSignos().EjecutarAsync(new(pacienteId, HospitalizacionId: otro.HospitalizacionActiva!.Id, UsuarioId: enfermera.Id));

        Assert.Equal((TipoError.NoEncontrado, "hospitalizacion-no-encontrada"), (resultado.Error.Tipo, resultado.Error.Codigo));
        Assert.Equal(0, ConsultasAuditadas());
    }
}

public class NotasTests
{
    private readonly Escenario _e = new();

    private async Task<(Guid PacienteId, Usuario Autor, Usuario Otro)> PacienteAsync()
    {
        var autor = _e.AgregarUsuario("medico@hospital.pe", "Segura123", Rol.Medico, "Luis Médico");
        var otro = _e.AgregarUsuario("enfermera@hospital.pe", "Segura123", Rol.Enfermera, "Rosa Enfermera");
        var ficha = await _e.IngresarAsync();
        return (ficha.Id, autor, otro);
    }

    [Fact]
    public async Task HU22_E1_La_nota_queda_con_fecha_hora_y_el_nombre_del_autor()
    {
        var (pacienteId, autor, _) = await PacienteAsync();

        var nota = (await _e.RegistrarNota().EjecutarAsync(new(pacienteId, "evolucion", "  Paciente afebril, tolera dieta.  ", autor.Id, "10.0.0.9"))).Valor;

        Assert.Equal(("Evolucion", "Paciente afebril, tolera dieta.", "Luis Médico", 1), (nota.Tipo, nota.Texto, nota.Autor.Nombre, nota.Version));
        Assert.Equal((_e.Reloj.AhoraUtc, _e.Reloj.AhoraUtc.AddHours(24), (DateTime?)null), (nota.CreadaEn, nota.EditableHasta, nota.EditadaEn));
        var auditada = Assert.Single(_e.Auditoria.De(AccionAuditoria.NotaRegistrada));
        Assert.DoesNotContain("afebril", auditada.Detalle);
    }

    [Theory]
    [InlineData(null, "Texto válido de la nota.", "tipo")]
    [InlineData("Receta", "Texto válido de la nota.", "tipo")]
    [InlineData("Evolucion", "abc", "texto")]
    [InlineData("Evolucion", null, "texto")]
    public async Task Tipo_y_texto_se_validan(string? tipo, string? texto, string campo)
    {
        var (pacienteId, autor, _) = await PacienteAsync();

        var resultado = await _e.RegistrarNota().EjecutarAsync(new(pacienteId, tipo, texto, autor.Id, null));

        Assert.Contains(campo, resultado.Error.Detalles.Keys);
    }

    [Fact]
    public async Task Sin_hospitalizacion_activa_no_se_registran_notas()
    {
        var (pacienteId, autor, _) = await PacienteAsync();
        await _e.RegistrarEgreso().EjecutarAsync(new(pacienteId, "AltaMedica", null, _e.EnfermeraId, null));

        var resultado = await _e.RegistrarNota().EjecutarAsync(new(pacienteId, "Evolucion", "Nota después del alta.", autor.Id, null));

        Assert.Equal((TipoError.Conflicto, "sin-hospitalizacion-activa"), (resultado.Error.Tipo, resultado.Error.Codigo));
    }

    [Fact]
    public async Task HU22_E2_Al_corregirla_guarda_la_version_anterior_y_la_fecha_de_la_edicion()
    {
        var (pacienteId, autor, _) = await PacienteAsync();
        var nota = (await _e.RegistrarNota().EjecutarAsync(new(pacienteId, "Evolucion", "Texto original de la nota.", autor.Id, null))).Valor;
        _e.Reloj.Avanzar(TimeSpan.FromHours(2));

        var editada = (await _e.EditarNota().EjecutarAsync(new(nota.Id, null, "Texto corregido de la nota.", autor.Id, null))).Valor;
        var versiones = (await _e.ListarVersionesNota().EjecutarAsync(new(nota.Id, autor.Id, null))).Valor;

        Assert.Equal(("Texto corregido de la nota.", 2, (DateTime?)_e.Reloj.AhoraUtc, "Evolucion"), (editada.Texto, editada.Version, editada.EditadaEn, editada.Tipo));
        var anterior = Assert.Single(versiones.Versiones);
        Assert.Equal((1, "Texto original de la nota.", nota.CreadaEn, _e.Reloj.AhoraUtc), (anterior.Numero, anterior.Texto, anterior.VigenteDesde, anterior.ReemplazadaEn));
        Assert.Single(_e.Auditoria.De(AccionAuditoria.NotaEditada));
        Assert.Single(_e.Auditoria.De(AccionAuditoria.ConsultaHistorialPaciente));
    }

    [Fact]
    public async Task Solo_el_autor_la_corrige_y_solo_dentro_de_24_horas()
    {
        var (pacienteId, autor, otro) = await PacienteAsync();
        var nota = (await _e.RegistrarNota().EjecutarAsync(new(pacienteId, "Evolucion", "Texto original de la nota.", autor.Id, null))).Valor;

        var ajena = await _e.EditarNota().EjecutarAsync(new(nota.Id, null, "Corrección de otra persona.", otro.Id, null));
        _e.Reloj.Avanzar(TimeSpan.FromHours(24).Add(TimeSpan.FromSeconds(1)));
        var tardia = await _e.EditarNota().EjecutarAsync(new(nota.Id, null, "Corrección tardía del autor.", autor.Id, null));

        Assert.Equal((TipoError.Prohibido, "nota-de-otro-autor"), (ajena.Error.Tipo, ajena.Error.Codigo));
        Assert.Equal((TipoError.Conflicto, "nota-fuera-de-plazo"), (tardia.Error.Tipo, tardia.Error.Codigo));
        Assert.Empty(_e.Notas.Versiones);
    }

    [Fact]
    public async Task Corregirla_sin_cambios_no_crea_version_ni_auditoria()
    {
        var (pacienteId, autor, _) = await PacienteAsync();
        var nota = (await _e.RegistrarNota().EjecutarAsync(new(pacienteId, "Enfermeria", "Texto original de la nota.", autor.Id, null))).Valor;

        var igual = (await _e.EditarNota().EjecutarAsync(new(nota.Id, "Enfermeria", "Texto original de la nota.", autor.Id, null))).Valor;

        Assert.Equal(1, igual.Version);
        Assert.Empty(_e.Notas.Versiones);
        Assert.Empty(_e.Auditoria.De(AccionAuditoria.NotaEditada));
    }

    [Fact]
    public async Task La_lista_trae_las_notas_del_episodio_la_mas_reciente_primero_y_se_audita()
    {
        var (pacienteId, autor, otro) = await PacienteAsync();
        await _e.RegistrarNota().EjecutarAsync(new(pacienteId, "Evolucion", "Primera nota del médico.", autor.Id, null));
        _e.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        await _e.RegistrarNota().EjecutarAsync(new(pacienteId, "Enfermeria", "Segunda nota de enfermería.", otro.Id, null));

        var notas = (await _e.ListarNotas().EjecutarAsync(new(pacienteId, null, otro.Id, null))).Valor.Notas;

        Assert.Equal([("Enfermeria", "Rosa Enfermera"), ("Evolucion", "Luis Médico")], notas.Select(n => (n.Tipo, n.Autor.Nombre)));
        Assert.Single(_e.Auditoria.De(AccionAuditoria.ConsultaHistorialPaciente));
    }
}

public class EsperaTrasAtenderTests
{
    private readonly Escenario _e = new();

    private async Task<Usuario> PacienteAsync()
    {
        var enfermera = _e.AgregarUsuario("enfermera@hospital.pe", "Segura123", Rol.Enfermera, "Enfermera de turno");
        await _e.HaceAsync(TimeSpan.FromMinutes(5), async () =>
        {
            var f = await _e.IngresarAsync();
            await _e.VincularAsync(f.Id, _e.AgregarDispositivo());
            return f;
        });
        return enfermera;
    }

    /// <summary>NEWS2 5 (medio): FR 21 (2), SpO2 93 (2) y FC 95 (1).</summary>
    private Task Media() => Lectura(fr: 21, spo2: 93, fc: 95);

    /// <summary>NEWS2 7 (alto).</summary>
    private Task Alta() => Lectura(fr: 25, spo2: 91, fc: 95);

    private async Task Lectura(int fr, int spo2, int fc)
    {
        _e.Reloj.Avanzar(TimeSpan.FromSeconds(10));
        await _e.RegistrarLectura().EjecutarAsync(_e.Lectura(new SignosMedidos(Fc: fc, Fr: fr, Spo2: spo2)));
    }

    private async Task AtenderAsync(Usuario enfermera)
    {
        var abierta = _e.Alertas.Alertas.Single(a => a.Tipo == TipoAlerta.Riesgo && a.EstaAbierta);
        Assert.True((await _e.AtenderAlerta().EjecutarAsync(new(abierta.Id, "Se avisó al médico de guardia.", enfermera.Id, null))).EsExito);
    }

    private List<AlertaClinica> Riesgo() => _e.Alertas.Alertas.Where(a => a.Tipo == TipoAlerta.Riesgo).ToList();

    [Fact]
    public async Task AL_E9_Tras_atender_no_se_reabre_en_el_mismo_nivel_pero_si_si_sube()
    {
        var enfermera = await PacienteAsync();
        await Media();
        Assert.Equal(PrioridadAlerta.Media, Assert.Single(Riesgo()).Prioridad);
        await AtenderAsync(enfermera);

        await Media();
        await Media();
        Assert.Single(Riesgo());

        await Alta();
        Assert.Equal(2, Riesgo().Count);
        Assert.Equal(PrioridadAlerta.Alta, Riesgo().Single(a => a.EstaAbierta).Prioridad);
    }

    [Fact]
    public async Task AL_E9_Al_terminar_la_espera_se_abre_aunque_siga_en_el_mismo_nivel()
    {
        var enfermera = await PacienteAsync();
        await Media();
        await AtenderAsync(enfermera);

        _e.Reloj.Avanzar(TimeSpan.FromMinutes(29));
        await Media();
        Assert.Single(Riesgo());

        _e.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await Media();
        Assert.Equal(2, Riesgo().Count);
    }

    [Fact]
    public async Task Con_la_espera_en_cero_se_reabre_en_la_evaluacion_siguiente()
    {
        _e.OpcionesAlertas = OpcionesAlertas.PorDefecto with { EsperaTrasAtender = TimeSpan.Zero };
        var enfermera = await PacienteAsync();
        await Media();
        await AtenderAsync(enfermera);

        await Media();

        Assert.Equal(2, Riesgo().Count);
    }
}

public class AuditoriaTests
{
    private readonly Escenario _e = new();

    [Fact]
    public async Task Devuelve_solo_acciones_e_ids_sin_el_texto_del_detalle()
    {
        var pacienteId = Guid.NewGuid();
        var hospitalizacionId = Guid.NewGuid();
        _e.Auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.PacienteEgresado, _e.Reloj.AhoraUtc, Guid.NewGuid(), "10.0.0.1",
            $"Paciente {pacienteId}, hospitalización {hospitalizacionId}, motivo Fallecimiento."));
        _e.Auditoria.Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.LoginFallido, _e.Reloj.AhoraUtc.AddMinutes(1), null, "10.0.0.2", "Correo: alguien@hospital.pe. Motivo: credenciales."));

        var pagina = (await _e.ListarAuditoria().EjecutarAsync(new())).Valor;

        Assert.Equal(["LoginFallido", "PacienteEgresado"], pagina.Elementos.Select(r => r.Accion));
        Assert.Empty(pagina.Elementos[0].Referencias);
        Assert.Equal([pacienteId, hospitalizacionId], pagina.Elementos[1].Referencias);
        Assert.DoesNotContain(typeof(Application.Auditoria.AuditoriaDto).GetProperties(), p => p.Name == "Detalle");
    }

    [Fact]
    public async Task Filtra_por_accion_y_rechaza_una_accion_desconocida()
    {
        _e.Auditoria.Agregar(RegistroAuditoria.Registrar(AccionAuditoria.AccesoDenegado, _e.Reloj.AhoraUtc, Guid.NewGuid()));
        _e.Auditoria.Agregar(RegistroAuditoria.Registrar(AccionAuditoria.LoginExitoso, _e.Reloj.AhoraUtc, Guid.NewGuid()));

        var filtrada = (await _e.ListarAuditoria().EjecutarAsync(new(Accion: "accesodenegado"))).Valor;
        var invalida = await _e.ListarAuditoria().EjecutarAsync(new(Accion: "BorrarTodo"));

        Assert.Equal("AccesoDenegado", Assert.Single(filtrada.Elementos).Accion);
        Assert.Contains("accion", invalida.Error.Detalles.Keys);
    }
}
