using Vitalify.Domain.Alertas;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Nnn;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Domain.Tests.Fase8;

/// <summary>Datos de prueba del motor NNN: un paciente con signos normales (NEWS2 0) que cada prueba altera.</summary>
internal static class PacienteNnn
{
    public static readonly DateTime Ahora = new(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc);
    public static readonly Guid Hospitalizacion = Guid.NewGuid();
    public static readonly Guid Paciente = Guid.NewGuid();

    public static readonly ParametrosClinicos Normales =
        new(Fr: 16, Spo2: 97, OxigenoSuplementario: false, Pas: 120, Fc: 80, Conciencia: NivelConciencia.Alerta, Temperatura: 36.8m);

    public static EvaluacionRiesgo Evaluar(ParametrosClinicos parametros, EscalaSpo2 escala = EscalaSpo2.Escala1) =>
        EvaluacionRiesgo.Calcular(Hospitalizacion, Paciente, Ahora, OrigenEvaluacion.Observacion, parametros, escala);

    public static AlertaClinica Alerta(TipoAlerta tipo) =>
        AlertaClinica.Crear(Hospitalizacion, Paciente, Guid.NewGuid(), tipo, PrioridadAlerta.Alta, null, null, null, null, Ahora);

    public static ObservacionEnfermeria ConDolor(int dolor) =>
        ObservacionEnfermeria.Registrar(Hospitalizacion, Paciente, Ahora, Ahora, Guid.NewGuid(),
            new DatosObservacion { Conciencia = NivelConciencia.Alerta, Contexto = ContextoClinico.Crear(dolor, null, null, null) },
            RangosFisiologicos.PorDefecto);

    public static IReadOnlyList<SugerenciaNnn> Sugerir(
        ParametrosClinicos? parametros = null, EscalaSpo2 escala = EscalaSpo2.Escala1, IReadOnlyList<AlertaClinica>? alertas = null,
        ObservacionEnfermeria? observacion = null, IReadOnlyCollection<string>? activos = null, IReadOnlyCollection<string>? descartados = null) =>
        MotorSugerenciasNnn.Estandar.Sugerir(new EntradaMotorNnn(
            Evaluar(parametros ?? Normales, escala), alertas ?? [], observacion, activos ?? [], descartados));

    public static SugerenciaNnn Unica(IReadOnlyList<SugerenciaNnn> sugerencias, string diagnostico) =>
        Assert.Single(sugerencias, s => s.DiagnosticoCodigo == diagnostico);
}

/// <summary>Una prueba por regla del motor (versión 2026.11.1) y los casos que el motor debe resolver.</summary>
public sealed class MotorSugerenciasNnnTests
{
    private static readonly ParametrosClinicos N = PacienteNnn.Normales;

    [Fact]
    public void Un_paciente_sin_puntos_no_dispara_nada() => Assert.Empty(PacienteNnn.Sugerir());

    [Fact]
    public void Sin_evaluacion_ni_alertas_ni_toma_no_dispara_nada() =>
        Assert.Empty(MotorSugerenciasNnn.Estandar.Sugerir(new EntradaMotorNnn(null, [], null, [])));

    [Fact]
    public void R1_FR_con_2_puntos_sugiere_patron_respiratorio_ineficaz()
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Fr = 22 }), "00032");

        Assert.Equal(["3350", "3140"], s.Intervenciones);
        Assert.Equal(["0415"], s.Resultados);
        Assert.Equal("R1", s.Regla);
        Assert.Equal("FR 22 rpm: 2 puntos NEWS2", s.Motivo);
        Assert.Equal(PrioridadSugerencia.Media, s.Prioridad);
    }

    [Fact]
    public void R1_FR_con_1_punto_no_alcanza() => Assert.Empty(PacienteNnn.Sugerir(N with { Fr = 10 }));

    [Fact]
    public void R2_SpO2_con_2_puntos_sugiere_deterioro_del_intercambio_de_gases()
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Spo2 = 93 }), "00030");

        Assert.Equal(["3320", "3350"], s.Intervenciones);
        Assert.Equal(["0402"], s.Resultados);
        Assert.Equal("SpO2 93 % (escala 1): 2 puntos NEWS2", s.Motivo);
    }

    [Fact]
    public void R2_tambien_se_dispara_con_oxigeno_suplementario_aunque_la_SpO2_sea_normal()
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { OxigenoSuplementario = true }), "00030");

        Assert.Equal("Recibe oxígeno suplementario", s.Motivo);
    }

    [Theory]
    [InlineData(38.1, true)]
    [InlineData(38.0, false)]
    public void R3_temperatura_de_38_1_o_mas_sugiere_hipertermia(double temperatura, bool sugiere)
    {
        var sugerencias = PacienteNnn.Sugerir(N with { Temperatura = (decimal)temperatura });

        if (sugiere)
        {
            var s = PacienteNnn.Unica(sugerencias, "00007");
            Assert.Equal(["3740", "6680"], s.Intervenciones);
            Assert.Equal(["0800"], s.Resultados);
            Assert.Equal("Temperatura 38,1 °C: 1 punto NEWS2", s.Motivo);
        }
        else
        {
            Assert.Empty(sugerencias);
        }
    }

    [Theory]
    [InlineData(35.0, true)]
    [InlineData(35.1, false)]
    public void R4_temperatura_de_35_o_menos_sugiere_hipotermia(double temperatura, bool sugiere)
    {
        var sugerencias = PacienteNnn.Sugerir(N with { Temperatura = (decimal)temperatura });

        Assert.Equal(sugiere, sugerencias.Any(s => s.DiagnosticoCodigo == "00006"));
        if (sugiere)
        {
            var s = PacienteNnn.Unica(sugerencias, "00006");
            Assert.Equal(["3800", "6680", "7710"], s.Intervenciones); // 3 puntos: también avisar al médico
            Assert.Equal(PrioridadSugerencia.Alta, s.Prioridad);
        }
    }

    [Fact]
    public void R5_solo_la_FC_con_2_puntos_sugiere_riesgo_de_disminucion_del_gasto_cardiaco()
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Fc = 115 }), "00240");

        Assert.Equal(["4150", "6680"], s.Intervenciones);
        Assert.Equal(["0400", "0802"], s.Resultados);
        Assert.DoesNotContain(PacienteNnn.Sugerir(N with { Fc = 115 }), x => x.DiagnosticoCodigo == "00029");
    }

    [Fact]
    public void R5_PAS_y_FC_con_2_puntos_sugieren_disminucion_del_gasto_cardiaco()
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Pas = 95, Fc = 115 }), "00029");

        Assert.Equal("PAS 95 mmHg: 2 puntos NEWS2; FC 115 lpm: 2 puntos NEWS2", s.Motivo);
    }

    [Fact]
    public void R6_PAS_de_90_o_menos_con_FC_de_131_o_mas_agrega_riesgo_de_deficit_de_volumen()
    {
        var sugerencias = PacienteNnn.Sugerir(N with { Pas = 90, Fc = 131 });

        var s = PacienteNnn.Unica(sugerencias, "00028");
        Assert.Equal(["4250", "4120", "7710"], s.Intervenciones);
        Assert.Equal(["0601"], s.Resultados);
        Assert.Contains("avise al médico", s.Motivo);
        Assert.DoesNotContain(PacienteNnn.Sugerir(N with { Pas = 91, Fc = 131 }), x => x.DiagnosticoCodigo == "00028");
    }

    [Theory]
    [InlineData(NivelConciencia.ConfusionNueva, "00128")]
    [InlineData(NivelConciencia.RespondeVoz, "00201")]
    [InlineData(NivelConciencia.NoResponde, "00201")]
    public void R7_ACVPU_distinto_de_A_sugiere_confusion_aguda_o_perfusion_cerebral(NivelConciencia nivel, string diagnostico)
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Conciencia = nivel }), diagnostico);

        Assert.Equal(["2620", "6490", "7710"], s.Intervenciones);
        Assert.Equal(["0912"], s.Resultados);
        Assert.Equal("R7", s.Regla);
    }

    [Fact]
    public void R8_una_caida_abierta_sugiere_riesgo_de_caidas_asociado_a_esa_alerta()
    {
        var caida = PacienteNnn.Alerta(TipoAlerta.Caida);

        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(alertas: [caida]), "00303");

        Assert.Equal(["6490"], s.Intervenciones);
        Assert.Equal(["1909"], s.Resultados);
        Assert.Equal(caida.Id, s.AlertaId);
        Assert.Equal(PrioridadSugerencia.Alta, s.Prioridad);
    }

    [Fact]
    public void R8_tambien_se_dispara_con_ACVPU_distinto_de_A() =>
        PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Conciencia = NivelConciencia.RespondeDolor }), "00303");

    [Theory]
    [InlineData(4, true)]
    [InlineData(3, false)]
    public void R9_dolor_de_4_o_mas_sugiere_dolor_agudo(int dolor, bool sugiere)
    {
        var sugerencias = PacienteNnn.Sugerir(observacion: PacienteNnn.ConDolor(dolor));

        if (sugiere)
        {
            var s = PacienteNnn.Unica(sugerencias, "00132");
            Assert.Equal(["1410"], s.Intervenciones);
            Assert.Equal(["2102"], s.Resultados);
            Assert.Equal("Dolor 4/10 en la toma manual", s.Motivo);
        }
        else
        {
            Assert.Empty(sugerencias);
        }
    }

    [Fact]
    public void R10_fiebre_con_taquicardia_y_taquipnea_sugiere_riesgo_de_infeccion_y_avisar_al_medico_sin_decir_sepsis()
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Temperatura = 38.5m, Fc = 95, Fr = 21 }), "00004");

        Assert.Equal(["6540", "7710"], s.Intervenciones);
        Assert.Equal(["0703"], s.Resultados);
        Assert.Contains("posible respuesta inflamatoria: avise al médico", s.Motivo);
        Assert.DoesNotContain("sepsis", s.Motivo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void R10_no_se_dispara_si_falta_uno_de_los_tres() =>
        Assert.DoesNotContain(PacienteNnn.Sugerir(N with { Temperatura = 38.5m, Fc = 95, Fr = 20 }), s => s.DiagnosticoCodigo == "00004");

    [Fact]
    public void R11_NEWS2_de_5_o_mas_agrega_7710_a_todas_las_sugerencias()
    {
        // FR 22 (2) + FC 115 (2) + temperatura 38,5 (1) = 5, ningún parámetro en 3.
        var sugerencias = PacienteNnn.Sugerir(N with { Fr = 22, Fc = 115, Temperatura = 38.5m });

        Assert.NotEmpty(sugerencias);
        Assert.All(sugerencias, s =>
        {
            Assert.Contains("7710", s.Intervenciones);
            Assert.Contains("R11", s.Reglas);
            Assert.EndsWith("NEWS2 5: avise al médico", s.Motivo);
        });
    }

    [Fact]
    public void R11_un_parametro_en_3_tambien_agrega_7710()
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Fr = 25 }), "00032");

        Assert.Equal(["3350", "3140", "7710"], s.Intervenciones);
        Assert.Equal(PrioridadSugerencia.Alta, s.Prioridad);
        Assert.EndsWith("NEWS2 3 con un parámetro en 3: avise al médico", s.Motivo);
    }

    [Fact]
    public void Con_NEWS2_bajo_no_se_agrega_7710() =>
        Assert.DoesNotContain("7710", PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Fr = 22 }), "00032").Intervenciones);

    [Fact]
    public void Dos_reglas_que_sugieren_el_mismo_diagnostico_dan_una_sola_sugerencia()
    {
        var a = new ReglaNnn("R1", "a", _ => new CandidataNnn("00032", ["3350"], ["0415"], "Motivo A", PrioridadSugerencia.Media));
        var b = new ReglaNnn("R2", "b", _ => new CandidataNnn("00032", ["3140", "3350"], ["0802"], "Motivo B", PrioridadSugerencia.Alta));
        var motor = new MotorSugerenciasNnn([a, b]);

        var s = Assert.Single(motor.Sugerir(new EntradaMotorNnn(PacienteNnn.Evaluar(N), [], null, [])));

        Assert.Equal(["3350", "3140"], s.Intervenciones);
        Assert.Equal(["0415", "0802"], s.Resultados);
        Assert.Equal(["R1", "R2"], s.Reglas);
        Assert.Equal("R1", s.Regla);
        Assert.Equal("Motivo A. Motivo B", s.Motivo);
        Assert.Equal(PrioridadSugerencia.Alta, s.Prioridad);
    }

    [Fact]
    public void Un_diagnostico_ya_activo_no_se_vuelve_a_sugerir()
    {
        var sugerencias = PacienteNnn.Sugerir(N with { Fr = 26, Spo2 = 90 }, activos: ["00032"]);

        Assert.DoesNotContain(sugerencias, s => s.DiagnosticoCodigo == "00032");
        PacienteNnn.Unica(sugerencias, "00030");
    }

    [Fact]
    public void Un_diagnostico_descartado_en_el_turno_tampoco_se_sugiere() =>
        Assert.Empty(PacienteNnn.Sugerir(N with { Fr = 22 }, descartados: ["00032"]));

    [Fact]
    public void Las_sugerencias_van_de_la_mayor_prioridad_a_la_menor_y_llevan_la_evaluacion()
    {
        var evaluacion = PacienteNnn.Evaluar(N with { Temperatura = 38.2m, Fr = 26 });

        var sugerencias = MotorSugerenciasNnn.Estandar.Sugerir(new EntradaMotorNnn(evaluacion, [], null, []));

        Assert.Equal(["00032", "00007"], sugerencias.Select(s => s.DiagnosticoCodigo)); // FR en 3 (alta) antes que la fiebre (baja)
        Assert.All(sugerencias, s => Assert.Equal(evaluacion.Id, s.EvaluacionId));
    }

    [Fact]
    public void Las_sugerencias_de_los_signos_se_asocian_a_la_alerta_de_riesgo_abierta()
    {
        var riesgo = PacienteNnn.Alerta(TipoAlerta.Riesgo);

        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Fr = 22 }, alertas: [riesgo]), "00032");

        Assert.Equal(riesgo.Id, s.AlertaId);
    }

    [Fact]
    public void Las_reglas_tienen_ids_unicos_y_la_version_es_la_publicada()
    {
        Assert.Equal("2026.11.1", MotorSugerenciasNnn.VersionReglas);
        Assert.Equal(
            ["R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", "R9", "R10"],
            MotorSugerenciasNnn.Estandar.Reglas.Select(r => r.Id));
        Assert.Throws<ArgumentException>(() => new MotorSugerenciasNnn([ReglasNnn.PatronRespiratorio, ReglasNnn.PatronRespiratorio]));
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(2, 2)]
    [InlineData(1, 3)]
    [InlineData(0, 5)]
    public void La_valoracion_NOC_sugerida_sale_de_los_puntos_NEWS2(int puntos, int puntuacion) =>
        Assert.Equal(puntuacion, ValoracionNocSugerida.DesdePuntosNews2(puntos));

    [Fact]
    public void La_valoracion_NOC_sugerida_nunca_propone_4_y_sin_dato_no_propone_nada()
    {
        Assert.DoesNotContain(4, Enumerable.Range(0, 4).Select(p => ValoracionNocSugerida.DesdePuntosNews2(p)));
        Assert.Null(ValoracionNocSugerida.DesdePuntosNews2(null));
        Assert.Null(ValoracionNocSugerida.Para(ParametroVital.Fr, null));
    }

    [Theory]
    [InlineData(ParametroVital.Fr, 1)]           // FR 26 → 3 puntos
    [InlineData(ParametroVital.Spo2, 1)]         // SpO2 90 → 3 puntos
    [InlineData(ParametroVital.Temperatura, 3)]  // 38,5 → 1 punto
    [InlineData(ParametroVital.Fc, 2)]           // 115 → 2 puntos
    [InlineData(ParametroVital.Pas, 5)]          // 120 → 0 puntos
    [InlineData(ParametroVital.Conciencia, 5)]   // A → 0 puntos
    public void La_valoracion_sugerida_de_un_indicador_vital_usa_los_puntos_de_su_signo(ParametroVital parametro, int esperada) =>
        Assert.Equal(esperada, ValoracionNocSugerida.Para(parametro, PacienteNnn.Evaluar(N with { Fr = 26, Spo2 = 90, Temperatura = 38.5m, Fc = 115 })));

    [Fact]
    public void Un_indicador_de_un_signo_que_no_se_midio_no_tiene_propuesta() =>
        Assert.Null(ValoracionNocSugerida.Para(ParametroVital.Pas, PacienteNnn.Evaluar(new ParametrosClinicos(Fr: 16, Spo2: 97))));
}
