using Vitalify.Domain.Alertas;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Servicios;
using Vitalify.Domain.Telemetria;
using Vitalify.Domain.Turnos;

namespace Vitalify.Domain.Tests.Fase6;

public class CatalogoTurnosTests
{
    private static readonly CatalogoTurnos Catalogo = new(CatalogoTurnos.HorariosPorDefecto, TimeZoneInfo.FindSystemTimeZoneById("America/Lima"));

    /// <summary>Hora de Lima (UTC−5) a UTC.</summary>
    private static DateTime Lima(int dia, int hora, int minuto = 0) => new DateTime(2026, 11, dia, hora, minuto, 0, DateTimeKind.Utc).AddHours(5);

    [Fact]
    public void La_guardia_nocturna_empieza_a_las_19_y_termina_a_las_07_del_dia_siguiente()
    {
        var intervalo = Catalogo.Intervalo(new TurnoDelDia(new DateOnly(2026, 11, 5), TipoTurno.GuardiaNocturna));

        Assert.Equal((Lima(5, 19), Lima(6, 7)), (intervalo.InicioUtc, intervalo.FinUtc));
    }

    [Theory]
    [InlineData(3, 0, 4, TipoTurno.GuardiaNocturna)] // 03:00 del día 5 → nocturna que empezó el 4
    [InlineData(6, 59, 4, TipoTurno.GuardiaNocturna)]
    [InlineData(7, 0, 5, TipoTurno.GuardiaDiurna)] // las 07:00 exactas ya son de la diurna
    [InlineData(18, 59, 5, TipoTurno.GuardiaDiurna)]
    [InlineData(19, 0, 5, TipoTurno.GuardiaNocturna)] // las 19:00 exactas ya son de la nocturna
    [InlineData(23, 30, 5, TipoTurno.GuardiaNocturna)]
    public void La_guardia_vigente_en_hora_de_Lima(int hora, int minuto, int diaDelTurno, TipoTurno tipo)
    {
        var guardias = Catalogo.TurnosEn(Lima(5, hora, minuto)).Where(t => t.Tipo is TipoTurno.GuardiaDiurna or TipoTurno.GuardiaNocturna);

        Assert.Equal(new TurnoDelDia(new DateOnly(2026, 11, diaDelTurno), tipo), Assert.Single(guardias));
    }

    [Fact]
    public void A_las_15_estan_la_guardia_diurna_y_el_turno_de_tarde()
    {
        Assert.Equal(
            [new TurnoDelDia(new DateOnly(2026, 11, 5), TipoTurno.GuardiaDiurna), new TurnoDelDia(new DateOnly(2026, 11, 5), TipoTurno.Tarde)],
            Catalogo.TurnosEn(Lima(5, 15)).OrderBy(t => t.Tipo));
    }

    [Fact]
    public void Detecta_los_turnos_que_empiezan_en_un_intervalo()
    {
        var turnos = Catalogo.TurnosQueEmpiezanEntre(Lima(5, 18, 59), Lima(5, 19, 0));

        Assert.Equal([new TurnoDelDia(new DateOnly(2026, 11, 5), TipoTurno.GuardiaNocturna)], turnos);
        Assert.Empty(Catalogo.TurnosQueEmpiezanEntre(Lima(5, 19, 0), Lima(5, 19, 30)));
    }

    [Fact]
    public void Turnos_que_se_tocan_no_se_cruzan()
    {
        var nocturna = Catalogo.Intervalo(new TurnoDelDia(new DateOnly(2026, 11, 5), TipoTurno.GuardiaNocturna));
        var diurnaDelSiguiente = Catalogo.Intervalo(new TurnoDelDia(new DateOnly(2026, 11, 6), TipoTurno.GuardiaDiurna));
        var tardeDelMismoDia = Catalogo.Intervalo(new TurnoDelDia(new DateOnly(2026, 11, 6), TipoTurno.Tarde));

        Assert.False(nocturna.SeCruzaCon(diurnaDelSiguiente));
        Assert.True(diurnaDelSiguiente.SeCruzaCon(tardeDelMismoDia));
    }
}

public class PlanillaMensualTests
{
    private static readonly CatalogoTurnos Catalogo = new(CatalogoTurnos.HorariosPorDefecto, TimeZoneInfo.FindSystemTimeZoneById("America/Lima"));
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Dictionary<string, Guid> Letras = new() { ["A"] = A, ["B"] = B, ["C"] = C };

    private static DiaPlanilla Dia(int dia, params (TipoTurno Tipo, string[] Letras)[] turnos) =>
        new(dia, turnos.ToDictionary(t => t.Tipo, t => (IReadOnlyList<string>)t.Letras));

    [Fact]
    public void Una_planilla_valida_produce_una_asignacion_por_letra_y_turno()
    {
        var resultado = PlanillaMensual.Validar(2026, 11, Letras,
            [Dia(1, (TipoTurno.GuardiaDiurna, ["C"]), (TipoTurno.GuardiaNocturna, ["A"])), Dia(2, (TipoTurno.GuardiaDiurna, ["a"]), (TipoTurno.GuardiaNocturna, ["B"]))],
            Catalogo);

        Assert.True(resultado.EsValida);
        Assert.Equal(4, resultado.Asignaciones.Count);
        Assert.Contains(resultado.Asignaciones, a => a.Letra == "A" && a.UsuarioId == A && a.Turno == new TurnoDelDia(new DateOnly(2026, 11, 2), TipoTurno.GuardiaDiurna));
    }

    [Fact]
    public void La_nocturna_de_un_dia_y_la_diurna_del_siguiente_se_permiten()
    {
        var resultado = PlanillaMensual.Validar(2026, 11, Letras,
            [Dia(5, (TipoTurno.GuardiaNocturna, ["A"])), Dia(6, (TipoTurno.GuardiaDiurna, ["A"]))], Catalogo);

        Assert.True(resultado.EsValida);
    }

    [Fact]
    public void Dos_turnos_que_se_cruzan_dan_error_por_dia_y_letra()
    {
        var resultado = PlanillaMensual.Validar(2026, 11, Letras,
            [Dia(3, (TipoTurno.GuardiaDiurna, ["B"]), (TipoTurno.Tarde, ["B"]))], Catalogo);

        Assert.False(resultado.EsValida);
        Assert.Empty(resultado.Asignaciones);
        Assert.Contains("se cruzan", Assert.Single(resultado.Errores["dias.3.B"]));
    }

    [Fact]
    public void Dos_letras_de_la_misma_enfermera_tambien_cuentan_como_cruce()
    {
        var letras = new Dictionary<string, Guid> { ["A"] = A, ["Z"] = A };

        var resultado = PlanillaMensual.Validar(2026, 11, letras, [Dia(4, (TipoTurno.GuardiaDiurna, ["A"]), (TipoTurno.Manana, ["Z"]))], Catalogo);

        Assert.False(resultado.EsValida);
    }

    [Fact]
    public void Dias_inexistentes_y_letras_sin_definir_son_errores()
    {
        var resultado = PlanillaMensual.Validar(2026, 2, Letras,
            [Dia(29, (TipoTurno.GuardiaDiurna, ["A"])), Dia(10, (TipoTurno.GuardiaNocturna, ["F"]))], Catalogo);

        Assert.Contains("dias.29", resultado.Errores.Keys);
        Assert.Contains("dias.10.F", resultado.Errores.Keys);
    }
}

public class AlertaClinicaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc);

    private static AlertaClinica Riesgo(PrioridadAlerta prioridad = PrioridadAlerta.Media, string nivel = "medio", int news2 = 5) =>
        AlertaClinica.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoAlerta.Riesgo, prioridad, nivel, news2, 2, null, Ahora);

    [Theory]
    [InlineData(NivelRiesgoNews2.Bajo, NivelRiesgoMews.Bajo, null)]
    [InlineData(NivelRiesgoNews2.BajoMedio, NivelRiesgoMews.Bajo, PrioridadAlerta.Baja)]
    [InlineData(NivelRiesgoNews2.Medio, NivelRiesgoMews.Bajo, PrioridadAlerta.Media)]
    [InlineData(NivelRiesgoNews2.Alto, NivelRiesgoMews.Bajo, PrioridadAlerta.Alta)]
    [InlineData(NivelRiesgoNews2.Bajo, NivelRiesgoMews.Medio, PrioridadAlerta.Media)]
    [InlineData(NivelRiesgoNews2.BajoMedio, NivelRiesgoMews.Alto, PrioridadAlerta.Alta)]
    [InlineData(NivelRiesgoNews2.Alto, NivelRiesgoMews.Medio, PrioridadAlerta.Alta)]
    public void La_prioridad_de_riesgo_es_la_mayor_entre_NEWS2_y_MEWS(NivelRiesgoNews2 news2, NivelRiesgoMews mews, PrioridadAlerta? esperada) =>
        Assert.Equal(esperada, PrioridadRiesgo.Calcular(news2, mews)?.Prioridad);

    [Fact]
    public void Si_sube_la_prioridad_se_actualiza_y_hay_que_volver_a_notificar()
    {
        var alerta = Riesgo();

        Assert.True(alerta.Actualizar(PrioridadAlerta.Alta, "alto", 7, 4, null, Ahora.AddMinutes(1)));
        Assert.Equal((PrioridadAlerta.Alta, "alto", (int?)7), (alerta.Prioridad, alerta.NivelRiesgo, alerta.News2));
    }

    [Fact]
    public void Si_baja_o_se_mantiene_se_guardan_los_valores_sin_notificar_y_la_prioridad_no_baja()
    {
        var alerta = Riesgo(PrioridadAlerta.Alta, "alto", 8);

        Assert.False(alerta.Actualizar(PrioridadAlerta.Media, "medio", 5, 2, null, Ahora.AddMinutes(1)));
        Assert.Equal((PrioridadAlerta.Alta, "alto", (int?)5), (alerta.Prioridad, alerta.NivelRiesgo, alerta.News2));
        Assert.False(alerta.Actualizar(PrioridadAlerta.Alta, "alto", 6, 2, null, Ahora.AddMinutes(2)));
    }

    [Fact]
    public void Tomar_y_atender_registran_los_tiempos_de_respuesta()
    {
        var alerta = Riesgo();
        var enfermera = Guid.NewGuid();

        alerta.Tomar(enfermera, Ahora.AddMinutes(2));
        Assert.Equal(EstadoAlerta.EnAtencion, alerta.Estado);
        Assert.Throws<ExcepcionDeDominio>(() => alerta.Tomar(Guid.NewGuid(), Ahora.AddMinutes(3)));

        alerta.Atender(enfermera, "  Se avisó al médico y se repitió la toma.  ", Ahora.AddMinutes(9));
        Assert.Equal((EstadoAlerta.Atendida, (Guid?)enfermera, (DateTime?)Ahora.AddMinutes(2), "Se avisó al médico y se repitió la toma."),
            (alerta.Estado, alerta.AtendidaPor, alerta.TomadaEn, alerta.NotaAtencion));
        Assert.False(alerta.EstaAbierta);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ok")]
    public void Atender_exige_una_nota_de_5_a_1000_caracteres(string nota)
    {
        Assert.Throws<ExcepcionDeDominio>(() => Riesgo().Atender(Guid.NewGuid(), nota, Ahora));
        Assert.Throws<ExcepcionDeDominio>(() => Riesgo().Atender(Guid.NewGuid(), new string('x', 1001), Ahora));
    }

    [Theory]
    [InlineData(TipoAlerta.SinSenal, true)]
    [InlineData(TipoAlerta.TomaPendiente, true)]
    [InlineData(TipoAlerta.Riesgo, false)]
    [InlineData(TipoAlerta.Caida, false)]
    [InlineData(TipoAlerta.PreocupacionClinica, false)]
    public void Solo_sin_senal_y_toma_pendiente_se_resuelven_solas(TipoAlerta tipo, bool seResuelve)
    {
        var alerta = AlertaClinica.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tipo, PrioridadAlerta.Media, null, null, null, null, Ahora);

        if (seResuelve)
        {
            alerta.ResolverAutomaticamente(Ahora.AddMinutes(1));
            Assert.Equal((EstadoAlerta.Atendida, AlertaClinica.NotaResolucionAutomatica, (Guid?)null), (alerta.Estado, alerta.NotaAtencion, alerta.AtendidaPor));
        }
        else
        {
            Assert.Throws<ExcepcionDeDominio>(() => alerta.ResolverAutomaticamente(Ahora.AddMinutes(1)));
        }
    }

    [Theory]
    [InlineData(PrioridadAlerta.Alta, 4, false)]
    [InlineData(PrioridadAlerta.Alta, 5, true)]
    [InlineData(PrioridadAlerta.Media, 14, false)]
    [InlineData(PrioridadAlerta.Media, 15, true)]
    [InlineData(PrioridadAlerta.Baja, 600, false)]
    public void Escala_la_alta_a_los_5_min_y_la_media_a_los_15_una_sola_vez(PrioridadAlerta prioridad, int minutos, bool escala)
    {
        var alerta = Riesgo(prioridad);

        Assert.Equal(escala, alerta.DebeEscalar(Ahora.AddMinutes(minutos), ReglasEscalamiento.PorDefecto));
        if (escala)
        {
            alerta.MarcarEscalada(Ahora.AddMinutes(minutos));
            Assert.False(alerta.DebeEscalar(Ahora.AddMinutes(minutos + 10), ReglasEscalamiento.PorDefecto));
        }
    }

    [Fact]
    public void Una_alerta_tomada_no_escala()
    {
        var alerta = Riesgo(PrioridadAlerta.Alta, "alto", 7);
        alerta.Tomar(Guid.NewGuid(), Ahora.AddMinutes(1));

        Assert.False(alerta.DebeEscalar(Ahora.AddMinutes(30), ReglasEscalamiento.PorDefecto));
    }

    [Fact]
    public void Las_notificaciones_se_acumulan()
    {
        var alerta = Riesgo();
        alerta.RegistrarNotificaciones([new NotificacionAlerta(Guid.NewGuid(), "push", "nueva", Ahora, "enviado")]);
        alerta.RegistrarNotificaciones([new NotificacionAlerta(Guid.NewGuid(), "tiempo-real", "subio", Ahora, "conectado")]);

        Assert.Equal(["nueva", "subio"], alerta.NotificadaA.Select(n => n.Motivo));
    }
}

public class VigenciaTomaManualTests
{
    private static readonly DateTime Toma = new(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(30);

    private static ObservacionEnfermeria Observar(DateTime observadaEn) =>
        ObservacionEnfermeria.Registrar(Guid.NewGuid(), Guid.NewGuid(), observadaEn, observadaEn, Guid.NewGuid(),
            18, 120, 80, null, NivelConciencia.Alerta, false, RangosFisiologicos.PorDefecto);

    private static EvaluacionRiesgo Evaluacion(DateTime evaluadaEn, DateTime proxima)
    {
        var e = EvaluacionRiesgo.Calcular(Guid.NewGuid(), Guid.NewGuid(), evaluadaEn, OrigenEvaluacion.Lectura, new ParametrosClinicos(Fc: 80));
        e.FijarProximaTomaManual(proxima);
        return e;
    }

    [Theory]
    [InlineData(4, 4.5)]   // NEWS2 1–4: próxima a las 4 h, vale 4 h 30 min
    [InlineData(1, 1.5)]   // NEWS2 5 o un parámetro en 3: 1 h 30 min
    [InlineData(12, 12)]   // NEWS2 0: 12 h + 30 min, pero el tope es 12 h
    public void Una_toma_vale_hasta_su_proxima_toma_mas_la_tolerancia_con_tope_de_12_horas(double horasHastaProxima, double horasVigente) =>
        Assert.Equal(Toma.AddHours(horasVigente), FrecuenciaTomaManual.VigenteHasta(Toma, Toma.AddHours(horasHastaProxima), Tolerancia));

    [Fact]
    public void Con_NEWS2_0_la_toma_sigue_contando_entre_las_4_y_las_12_horas()
    {
        var toma = Observar(Toma);
        toma.FijarVigencia(FrecuenciaTomaManual.VigenteHasta(Toma, Toma.AddHours(12), Tolerancia));

        Assert.True(FrecuenciaTomaManual.Cuenta(toma, Toma.AddHours(8), null, Tolerancia));
        Assert.False(FrecuenciaTomaManual.Cuenta(toma, Toma.AddHours(12).AddMinutes(1), null, Tolerancia));
    }

    [Fact]
    public void Si_el_paciente_empeora_una_evaluacion_posterior_acorta_el_plazo()
    {
        var toma = Observar(Toma);
        toma.FijarVigencia(Toma.AddHours(12));
        var empeoro = Evaluacion(Toma.AddHours(2), proxima: Toma.AddHours(1));

        Assert.False(FrecuenciaTomaManual.Cuenta(toma, Toma.AddHours(2), empeoro, Tolerancia));
        Assert.True(FrecuenciaTomaManual.Cuenta(toma, Toma.AddHours(1).AddMinutes(20), empeoro, Tolerancia));
    }

    [Fact]
    public void Una_evaluacion_anterior_a_la_toma_no_la_acorta()
    {
        var toma = Observar(Toma);
        var anterior = Evaluacion(Toma.AddMinutes(-5), proxima: Toma.AddMinutes(-5));

        Assert.True(FrecuenciaTomaManual.Cuenta(toma, Toma.AddMinutes(1), anterior, Tolerancia));
    }

    [Fact]
    public void Las_tomas_sin_plazo_guardado_valen_4_horas()
    {
        var toma = Observar(Toma);

        Assert.True(FrecuenciaTomaManual.Cuenta(toma, Toma.AddHours(4), null, Tolerancia));
        Assert.False(FrecuenciaTomaManual.Cuenta(toma, Toma.AddHours(4).AddSeconds(1), null, Tolerancia));
    }
}

public class ServicioTests
{
    [Theory]
    [InlineData("Medicina B", "MEDICINA-B")]
    [InlineData("  Unidad de Vigilancia Intensiva  ", "UNIDAD-DE-VIGILANCIA-INTENSIVA")]
    [InlineData("Emergencia (Pediatría)", "EMERGENCIA-PEDIATRIA")]
    public void El_codigo_se_deriva_del_nombre_sin_tildes(string nombre, string codigo) =>
        Assert.Equal(codigo, Servicio.CodigoDesdeNombre(nombre));

    [Fact]
    public void Registrar_normaliza_el_codigo()
    {
        var servicio = Servicio.Registrar(" uvi ", " UVI ");

        Assert.Equal(("UVI", "UVI", true), (servicio.Codigo, servicio.Nombre, servicio.Activo));
        Assert.Throws<ExcepcionDeDominio>(() => Servicio.Registrar("U", "Muy corto"));
    }
}
