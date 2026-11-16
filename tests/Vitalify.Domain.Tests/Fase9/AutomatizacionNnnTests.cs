using Vitalify.Domain.Clinica;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Nnn;
using Vitalify.Domain.Notas;
using Vitalify.Domain.Telemetria;
using Vitalify.Domain.Tests.Fase8;
using Vitalify.Domain.Turnos;

namespace Vitalify.Domain.Tests.Fase9;

/// <summary>Precisión del wearable y reglas NNN 2026.12.1: mediana, fuente, persistencia, "por confirmar", R2 y R11.</summary>
public sealed class PrecisionYReglasTests
{
    private static readonly ParametrosClinicos N = PacienteNnn.Normales;
    private static readonly DateTime Ahora = PacienteNnn.Ahora;

    private static IReadOnlyList<SugerenciaNnn> Sugerir(
        ParametrosClinicos p, IReadOnlyList<EvaluacionRiesgo>? anteriores = null, FuentesSignos? fuentes = null, DateTime? instante = null,
        EscalaSpo2 escala = EscalaSpo2.Escala1) =>
        MotorSugerenciasNnn.Estandar.Sugerir(new EntradaMotorNnn(
            PacienteNnn.EvaluarEn(p, Ahora, escala, fuentes), [], null, [], null, anteriores, instante));

    [Fact]
    public void La_mediana_ignora_los_faltantes_y_con_dos_centrales_promedia()
    {
        Assert.Equal(80, PrecisionSensor.Mediana(new int?[] { 80, null, 140, 79 }));
        Assert.Equal(95, PrecisionSensor.Mediana(new int?[] { 97, 95, 95, 94 }));
        Assert.Null(PrecisionSensor.Mediana(Array.Empty<int?>()));
        Assert.Equal(38.3m, PrecisionSensor.Mediana(new decimal?[] { 38.2m, 38.4m }));
        Assert.False(PrecisionSensor.TemperaturaPielPuntua(36.0m));
        Assert.True(PrecisionSensor.TemperaturaPielPuntua(36.1m));
    }

    [Fact]
    public void La_evaluacion_sabe_de_que_fuente_salio_cada_valor()
    {
        var e = PacienteNnn.EvaluarEn(N, Ahora, fuentes: new FuentesSignos(FuenteSigno.Sensor, FuenteSigno.Manual, FuenteSigno.Manual));

        Assert.Equal(FuenteSigno.Sensor, e.FuenteDe(ParametroClinico.Fc));
        Assert.Equal(FuenteSigno.Sensor, e.FuenteDe(ParametroClinico.Spo2));
        Assert.Equal(FuenteSigno.Sensor, e.FuenteDe(ParametroClinico.Fr));
        Assert.Equal(FuenteSigno.Manual, e.FuenteDe(ParametroClinico.Pas));
        Assert.Equal(FuenteSigno.Manual, e.FuenteDe(ParametroClinico.Conciencia));
        Assert.Null(PacienteNnn.EvaluarEn(N with { Fr = null }, Ahora, fuentes: new FuentesSignos(FuenteSigno.Sensor)).FuenteDe(ParametroClinico.Fr));
    }

    [Fact]
    public void Un_criterio_solo_del_wearable_no_se_sugiere_hasta_que_persiste_2_minutos()
    {
        var spo2 = N with { Spo2 = 93 }; // 2 puntos

        Assert.Empty(Sugerir(spo2));
        Assert.Empty(Sugerir(spo2, [PacienteNnn.EvaluarEn(spo2, Ahora.AddSeconds(-90))]));
        var s = PacienteNnn.Unica(Sugerir(spo2, [PacienteNnn.EvaluarEn(spo2, Ahora.AddSeconds(-130))]), "00030");

        Assert.True(s.PorConfirmar);
        Assert.Equal([ParametroClinico.Spo2], s.ConfirmarCon);
    }

    [Fact]
    public void Con_un_parametro_en_3_basta_un_minuto()
    {
        var grave = N with { Spo2 = 90 }; // 3 puntos

        Assert.Empty(Sugerir(grave, [PacienteNnn.EvaluarEn(grave, Ahora.AddSeconds(-50))]));
        Assert.Single(Sugerir(grave, [PacienteNnn.EvaluarEn(grave, Ahora.AddSeconds(-61))]), x => x.DiagnosticoCodigo == "00030");
    }

    [Fact]
    public void La_persistencia_se_corta_si_en_medio_hubo_una_evaluacion_sin_el_criterio()
    {
        var spo2 = N with { Spo2 = 93 };
        var anteriores = new[]
        {
            PacienteNnn.EvaluarEn(spo2, Ahora.AddSeconds(-60)),
            PacienteNnn.EvaluarEn(N, Ahora.AddSeconds(-120)),
            PacienteNnn.EvaluarEn(spo2, Ahora.AddSeconds(-300)),
        };

        Assert.Empty(Sugerir(spo2, anteriores));
    }

    [Fact]
    public void La_consulta_extiende_la_persistencia_hasta_5_minutos_despues_de_la_ultima_evaluacion_guardada()
    {
        var spo2 = N with { Spo2 = 93 };

        Assert.Single(Sugerir(spo2, instante: Ahora.AddMinutes(3)));
        Assert.Empty(Sugerir(spo2, instante: Ahora.AddMinutes(1)));
    }

    [Fact]
    public void Un_criterio_de_la_toma_manual_se_sugiere_enseguida_y_no_queda_por_confirmar()
    {
        var s = PacienteNnn.Unica(Sugerir(N with { Fr = 22 }, fuentes: new FuentesSignos(Fr: FuenteSigno.Manual)), "00032");

        Assert.False(s.PorConfirmar);
        Assert.Null(s.ConfirmarCon);
    }

    [Fact]
    public void La_fiebre_de_la_piel_pide_10_minutos_y_termometro_la_del_termometro_no()
    {
        var fiebre = N with { Temperatura = 38.5m };
        var piel = new FuentesSignos(Temperatura: FuenteSigno.Sensor);

        Assert.Empty(Sugerir(fiebre, [PacienteNnn.EvaluarEn(fiebre, Ahora.AddMinutes(-5), fuentes: piel)], piel));
        var conPiel = PacienteNnn.Unica(Sugerir(fiebre, [PacienteNnn.EvaluarEn(fiebre, Ahora.AddMinutes(-11), fuentes: piel)], piel), "00007");
        Assert.True(conPiel.PorConfirmar);
        Assert.Equal([ParametroClinico.Temperatura], conPiel.ConfirmarCon);

        var conTermometro = PacienteNnn.Unica(Sugerir(fiebre, fuentes: new FuentesSignos(Temperatura: FuenteSigno.Manual)), "00007");
        Assert.False(conTermometro.PorConfirmar);
    }

    [Fact]
    public void R2_en_escala_2_con_oxigeno_y_SpO2_alta_avisa_exceso_de_oxigeno_y_propone_avisar_al_medico()
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { Spo2 = 97, OxigenoSuplementario = true }, EscalaSpo2.Escala2), "00030");

        Assert.Contains("sobre el objetivo 88–92 %", s.Motivo, StringComparison.Ordinal);
        Assert.Contains("3320", s.Intervenciones);
        Assert.Contains(ReglasNnn.NicColaboracionMedica, s.Intervenciones);
        Assert.DoesNotContain("deterioro", s.Motivo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void R2_solo_por_oxigeno_sale_con_prioridad_baja()
    {
        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(N with { OxigenoSuplementario = true }), "00030");

        Assert.Equal(PrioridadSugerencia.Baja, s.Prioridad);
        Assert.False(s.PorConfirmar);
    }

    [Fact]
    public void R11_tambien_con_MEWS_de_5_aunque_NEWS2_sea_bajo()
    {
        var p = N with { Fr = 18, Fc = 115, Pas = 210 }; // MEWS 1 + 2 + 2 = 5; NEWS2 2 (solo la FC)
        var e = PacienteNnn.Evaluar(p);
        Assert.Equal((2, 5), (e.News2Total, e.MewsTotal));

        var s = PacienteNnn.Unica(PacienteNnn.Sugerir(p), "00240");
        Assert.Contains(ReglasNnn.NicColaboracionMedica, s.Intervenciones);
        Assert.Contains(ReglasNnn.ColaboracionMedica, s.Reglas);
    }

    [Fact]
    public void A_igual_prioridad_va_primero_lo_respiratorio_y_luego_lo_circulatorio()
    {
        var s = PacienteNnn.Sugerir(N with { Fc = 115, Fr = 22 }); // R5 y R1, ambas con 2 puntos

        Assert.Equal(["00032", "00240"], s.Select(x => x.DiagnosticoCodigo).Take(2));
    }
}

/// <summary>Protocolo del paquete y de la agenda (versión 2026.12.1).</summary>
public sealed class ProtocoloYAgendaTests
{
    private static readonly DateTime Ahora = PacienteNnn.Ahora;
    private static readonly IntervaloTurno Guardia = new(Ahora.AddHours(-3), Ahora.AddHours(9));

    [Theory]
    [InlineData(16, 97, 80, NivelRespuestaNews2.Cero)]
    [InlineData(22, 97, 80, NivelRespuestaNews2.Bajo)]
    [InlineData(26, 97, 80, NivelRespuestaNews2.UnParametroEnTres)]
    [InlineData(22, 93, 80, NivelRespuestaNews2.Bajo)]
    [InlineData(22, 93, 115, NivelRespuestaNews2.Medio)]
    [InlineData(26, 91, 115, NivelRespuestaNews2.Alto)]
    public void El_nivel_de_respuesta_sale_del_NEWS2(int fr, int spo2, int fc, NivelRespuestaNews2 nivel) =>
        Assert.Equal(nivel, ProtocoloNnn.Nivel(PacienteNnn.Evaluar(PacienteNnn.Normales with { Fr = fr, Spo2 = spo2, Fc = fc }).News2()));

    [Fact]
    public void El_nivel_efectivo_es_el_mayor_desde_la_ultima_toma_completa()
    {
        var bajo = PacienteNnn.Evaluar(PacienteNnn.Normales with { Fr = 22 });
        var alto = PacienteNnn.Evaluar(PacienteNnn.Normales with { Fr = 26, Spo2 = 91, Fc = 115 });

        Assert.Equal(NivelRespuestaNews2.Alto, ProtocoloNnn.NivelEfectivo([bajo, alto, bajo]));
        Assert.Equal(NivelRespuestaNews2.Cero, ProtocoloNnn.NivelEfectivo([]));
    }

    [Theory]
    [InlineData("6680", FrecuenciaIntervencion.PorProtocolo)]
    [InlineData("3350", FrecuenciaIntervencion.PorProtocolo)]
    [InlineData("7710", FrecuenciaIntervencion.UnaVez)]
    [InlineData("6490", FrecuenciaIntervencion.CadaTurno)]
    [InlineData("5820", FrecuenciaIntervencion.CadaTurno)]
    public void La_frecuencia_del_paquete_sale_del_protocolo(string nic, FrecuenciaIntervencion frecuencia) =>
        Assert.Equal(frecuencia, ProtocoloNnn.FrecuenciaSugerida(nic));

    [Fact]
    public void La_tabla_sube_la_frecuencia_con_el_nivel()
    {
        Assert.True(ProtocoloNnn.PautaDe("3350", NivelRespuestaNews2.Cero).PorTurno);
        Assert.Equal(TimeSpan.FromHours(4), ProtocoloNnn.PautaDe("3350", NivelRespuestaNews2.Bajo).Intervalo);
        Assert.Equal(TimeSpan.FromHours(1), ProtocoloNnn.PautaDe("3350", NivelRespuestaNews2.Medio).Intervalo);
        Assert.Equal(TimeSpan.FromMinutes(30), ProtocoloNnn.PautaDe("3350", NivelRespuestaNews2.Alto).Intervalo);
        Assert.Equal(TimeSpan.FromHours(12), ProtocoloNnn.PautaDe("6680", NivelRespuestaNews2.Cero).Intervalo);
        Assert.True(ProtocoloNnn.PautaDe("3140", NivelRespuestaNews2.Bajo).SegunNecesidad);
        Assert.True(ProtocoloNnn.PautaDe("9999", NivelRespuestaNews2.Alto).PorTurno);
    }

    [Theory]
    [InlineData(1, false, 4)]
    [InlineData(2, false, 4)]
    [InlineData(3, false, 5)]
    [InlineData(5, false, 5)]
    [InlineData(3, true, 4)]
    [InlineData(5, true, 5)]
    public void La_meta_por_defecto_es_4_o_5_nunca_menor_que_la_basal(int basal, bool escala2, int meta) =>
        Assert.Equal(meta, ProtocoloNnn.MetaSugerida(basal, escala2));

    [Theory]
    [InlineData(0, 5)]
    [InlineData(3, 4)]
    [InlineData(5, 3)]
    [InlineData(8, 2)]
    [InlineData(10, 1)]
    public void El_dolor_se_pasa_a_la_escala_NOC(int dolor, int noc) => Assert.Equal(noc, ProtocoloNnn.NocDesdeDolor(dolor));

    [Fact]
    public void La_basal_es_la_mediana_de_los_puntos_de_30_minutos_no_un_valor_suelto()
    {
        var normal = PacienteNnn.Evaluar(PacienteNnn.Normales);
        var fr22 = PacienteNnn.Evaluar(PacienteNnn.Normales with { Fr = 22 });

        Assert.Equal(2, ProtocoloNnn.PuntuacionDesdeSignos("0415", [fr22, fr22, normal], null)); // mediana 2 puntos → 2
        Assert.Equal(5, ProtocoloNnn.PuntuacionDesdeSignos("0415", [normal, normal, fr22], null)); // mediana 0 → 5
        Assert.Equal(2, ProtocoloNnn.PuntuacionDesdeSignos("2102", [], 7));
        Assert.Null(ProtocoloNnn.PuntuacionDesdeSignos("1909", [normal], null));
    }

    private static IntervencionPlan Intervencion(string nic, FrecuenciaIntervencion f, DateTime? agregada = null) =>
        IntervencionPlan.Planificar(Guid.NewGuid(), nic, f, Guid.NewGuid(), agregada ?? Ahora.AddHours(-6));

    [Fact]
    public void Una_NIC_por_turno_sin_hacer_vence_una_hora_antes_del_fin_de_la_guardia()
    {
        var i = Intervencion("6490", FrecuenciaIntervencion.CadaTurno);

        var pendiente = AgendaIntervenciones.Calcular(i, null, NivelRespuestaNews2.Bajo, Guardia, null, Ahora);
        Assert.Equal((Guardia.FinUtc.AddHours(-1), EstadoAgenda.Proxima), (pendiente.ProximaEn!.Value, pendiente.Estado));

        var vencida = AgendaIntervenciones.Calcular(i, null, NivelRespuestaNews2.Bajo, Guardia, null, Guardia.FinUtc.AddMinutes(-30));
        Assert.Equal(EstadoAgenda.Vencida, vencida.Estado);

        Assert.Equal(EstadoAgenda.AlDia, AgendaIntervenciones.Calcular(i, Ahora.AddHours(-1), NivelRespuestaNews2.Bajo, Guardia, null, Ahora).Estado);
    }

    [Fact]
    public void Por_protocolo_la_proxima_depende_del_nivel_y_la_6680_usa_el_reloj_de_la_toma_manual()
    {
        var resp = Intervencion("3350", FrecuenciaIntervencion.PorProtocolo);
        var hecha = Ahora.AddMinutes(-40);

        var alto = AgendaIntervenciones.Calcular(resp, hecha, NivelRespuestaNews2.Alto, Guardia, null, Ahora);
        Assert.Equal((hecha.AddMinutes(30), EstadoAgenda.Vencida), (alto.ProximaEn!.Value, alto.Estado));
        var bajo = AgendaIntervenciones.Calcular(resp, hecha, NivelRespuestaNews2.Bajo, Guardia, null, Ahora);
        Assert.Equal((hecha.AddHours(4), EstadoAgenda.AlDia), (bajo.ProximaEn!.Value, bajo.Estado));

        var signos = Intervencion("6680", FrecuenciaIntervencion.PorProtocolo);
        Assert.Equal(Ahora.AddMinutes(20), AgendaIntervenciones.Calcular(signos, hecha, NivelRespuestaNews2.Alto, Guardia, Ahora.AddMinutes(20), Ahora).ProximaEn);
    }

    [Fact]
    public void Segun_necesidad_no_tiene_hora_y_una_vez_toca_desde_que_se_planifico()
    {
        Assert.Equal(EstadoAgenda.SegunNecesidad,
            AgendaIntervenciones.Calcular(Intervencion("3140", FrecuenciaIntervencion.SegunNecesidad), null, NivelRespuestaNews2.Alto, Guardia, null, Ahora).Estado);
        Assert.Equal(EstadoAgenda.Vencida,
            AgendaIntervenciones.Calcular(Intervencion("7710", FrecuenciaIntervencion.UnaVez), null, NivelRespuestaNews2.Alto, Guardia, null, Ahora).Estado);
    }

    [Fact]
    public void La_toma_manual_solo_registra_la_6680()
    {
        var signos = Intervencion("6680", FrecuenciaIntervencion.PorProtocolo);
        Assert.Equal(OrigenEjecucion.TomaManual, signos.Ejecutar(Guid.NewGuid(), Ahora, null, null, OrigenEjecucion.TomaManual).Origen);

        Assert.Throws<ExcepcionDeDominio>(() =>
            Intervencion("3350", FrecuenciaIntervencion.PorProtocolo).Ejecutar(Guid.NewGuid(), Ahora, null, null, OrigenEjecucion.TomaManual));
    }
}

/// <summary>Histéresis de las sugerencias, "listo para resolver" y la nota del PAE del turno.</summary>
public sealed class ResolucionYNotaTests
{
    private static readonly DateTime Ahora = PacienteNnn.Ahora;
    private static readonly Guid H = PacienteNnn.Hospitalizacion;
    private static readonly Guid P = PacienteNnn.Paciente;

    [Fact]
    public void Una_sugerencia_que_el_motor_deja_de_proponer_espera_30_minutos_salvo_que_una_toma_la_descarte()
    {
        var s = SugerenciaEmitida.Emitir(H, P, PacienteNnn.Unica(PacienteNnn.Sugerir(PacienteNnn.Normales with { Fr = 22 }), "00032"), "2026.12.1", Ahora);

        Assert.False(s.DebeExpirar(Ahora.AddMinutes(29), false));
        Assert.True(s.DebeExpirar(Ahora.AddMinutes(30), false));
        Assert.True(s.DebeExpirar(Ahora.AddMinutes(1), true));

        s.SeguirPropuesta(null, Ahora.AddMinutes(20));
        Assert.False(s.DebeExpirar(Ahora.AddMinutes(45), false));
    }

    private static DiagnosticoPlan Diagnostico(string codigo = "00032", string regla = "R1", double horas = 13) =>
        DiagnosticoPlan.Confirmar(H, P, codigo, OrigenDiagnostico.Sugerido, regla, null, null, "2026.12.1", Guid.NewGuid(), Ahora.AddHours(-horas));

    private static ObservacionEnfermeria Toma(double horasAtras, int? dolor = null, bool? antipiretico = null) =>
        ObservacionEnfermeria.Registrar(H, P, Ahora.AddHours(-horasAtras), Ahora.AddHours(-horasAtras), Guid.NewGuid(),
            new DatosObservacion
            {
                Fr = 16, Pas = 120, Pad = 80, Conciencia = NivelConciencia.Alerta, OxigenoSuplementario = false,
                Contexto = ContextoClinico.Crear(dolor, null, antipiretico, null),
            },
            RangosFisiologicos.PorDefecto);

    private static IReadOnlyList<EvaluacionRiesgo> Normales(int horas = 12) =>
        Enumerable.Range(0, horas).Select(i => PacienteNnn.EvaluarEn(PacienteNnn.Normales, Ahora.AddHours(-i))).ToList();

    private static string? Listo(
        DiagnosticoPlan? d = null, IReadOnlyList<EvaluacionRiesgo>? evals = null, IReadOnlyList<ObservacionEnfermeria>? tomas = null,
        double? ultimaNoc = 4, bool alerta = false) =>
        CriterioResolucion.ListoParaResolver(new DatosResolucion(
            d ?? Diagnostico(), evals ?? Normales(), tomas ?? [Toma(1), Toma(6)], [new EstadoResultadoNoc(ultimaNoc, 4)], alerta, Ahora));

    [Fact]
    public void Se_propone_resolver_tras_12_h_sin_el_criterio_con_2_tomas_completas_y_la_meta_NOC()
    {
        var motivo = Listo();

        Assert.NotNull(motivo);
        Assert.Contains("R1", motivo, StringComparison.Ordinal);
    }

    [Fact]
    public void No_se_propone_si_falta_algo()
    {
        Assert.Null(Listo(alerta: true));
        Assert.Null(Listo(d: Diagnostico(horas: 2)));
        Assert.Null(Listo(tomas: [Toma(1)]));
        Assert.Null(Listo(tomas: [Toma(1), Toma(3)])); // menos de 4 h entre las tomas
        Assert.Null(Listo(ultimaNoc: 3));
        Assert.Null(Listo(ultimaNoc: null));
        Assert.Null(Listo(evals: [.. Normales(), PacienteNnn.EvaluarEn(PacienteNnn.Normales with { Fr = 22 }, Ahora.AddHours(-5))]));
        Assert.Null(Listo(evals: [PacienteNnn.EvaluarEn(PacienteNnn.Normales with { Pas = null }, Ahora)])); // puntaje parcial
    }

    [Fact]
    public void Nunca_para_el_riesgo_de_caidas_ni_para_un_diagnostico_elegido_a_mano()
    {
        Assert.Null(Listo(d: Diagnostico("00303", "R8")));
        var manual = DiagnosticoPlan.Confirmar(H, P, "00146", OrigenDiagnostico.Manual, null, null, null, "2026.12.1", Guid.NewGuid(), Ahora.AddDays(-1));
        Assert.Null(Listo(d: manual));
    }

    [Fact]
    public void La_fiebre_pide_24_h_y_sin_antipiretico_y_el_dolor_3_o_menos_en_las_2_ultimas_tomas()
    {
        Assert.Null(Listo(d: Diagnostico("00007", "R3", 20)));
        Assert.NotNull(Listo(d: Diagnostico("00007", "R3", 25), evals: Normales(24)));
        Assert.Null(Listo(d: Diagnostico("00007", "R3", 25), evals: Normales(24), tomas: [Toma(1), Toma(6, antipiretico: true)]));

        Assert.NotNull(Listo(d: Diagnostico("00132", "R9"), tomas: [Toma(1, dolor: 2), Toma(6, dolor: 3)]));
        Assert.Null(Listo(d: Diagnostico("00132", "R9"), tomas: [Toma(1, dolor: 5), Toma(6, dolor: 3)]));
    }

    [Fact]
    public void La_nota_del_turno_lleva_su_guardia_y_no_cambia_de_tipo()
    {
        var nota = NotaClinica.RegistrarDeTurno(H, P, Guid.NewGuid(), "2026-11-09/GuardiaDiurna", "1. Valoración: estable.", Ahora);
        Assert.Equal((TipoNota.ProcesoEnfermeria, "2026-11-09/GuardiaDiurna"), (nota.Tipo, nota.Guardia));

        Assert.Throws<ExcepcionDeDominio>(() => NotaClinica.Registrar(H, P, Guid.NewGuid(), TipoNota.ProcesoEnfermeria, "Texto de prueba.", Ahora));
        Assert.Throws<ExcepcionDeDominio>(() => nota.Editar(nota.AutorId, TipoNota.Evolucion, "Otro texto.", Ahora));
        Assert.NotNull(nota.Editar(nota.AutorId, TipoNota.ProcesoEnfermeria, "1. Valoración: estable, sin dolor.", Ahora));
    }

    [Fact]
    public void El_borrador_tiene_los_cinco_pasos_el_aviso_del_sensor_y_no_pasa_de_4000_caracteres()
    {
        var texto = RedactorNotaPae.Redactar("Nota del turno · Cama MED-B-01",
            new SeccionesNotaPae(["NEWS2: 3 al inicio, peor 7, ahora 4."], ["00032 Patrón respiratorio ineficaz."], [], [], []));

        Assert.Contains("1. Valoración", texto, StringComparison.Ordinal);
        Assert.Contains("5. Evaluación", texto, StringComparison.Ordinal);
        Assert.Contains("Sin registros en el turno.", texto, StringComparison.Ordinal);
        Assert.EndsWith(RedactorNotaPae.AvisoBorrador, texto, StringComparison.Ordinal);

        var largo = RedactorNotaPae.Redactar("Nota", new SeccionesNotaPae(
            Enumerable.Range(0, 200).Select(i => $"Línea de valoración número {i} con datos del turno.").ToList(), [], [], [], []));
        Assert.True(largo.Length <= NotaClinica.LargoMaximoTexto);
        Assert.Contains("se omitieron líneas", largo, StringComparison.Ordinal);
        Assert.EndsWith(RedactorNotaPae.AvisoBorrador, largo, StringComparison.Ordinal);
        Assert.Equal("38,4", RedactorNotaPae.Numero(38.4m));
    }

    [Fact]
    public void Confirmar_un_paquete_guarda_si_fue_sin_cambios_y_la_version_del_protocolo()
    {
        var d = DiagnosticoPlan.Confirmar(H, P, "00032", OrigenDiagnostico.Sugerido, "R1", null, null, "2026.12.1", Guid.NewGuid(), Ahora, true, ProtocoloNnn.Version);
        Assert.Equal((true, "2026.12.1"), (d.ConfirmadoSinCambios, d.VersionProtocolo));

        var sinPaquete = DiagnosticoPlan.Confirmar(H, P, "00032", OrigenDiagnostico.Manual, null, null, null, "2026.12.1", Guid.NewGuid(), Ahora);
        Assert.Equal(((bool?)null, (string?)null), (sinPaquete.ConfirmadoSinCambios, sinPaquete.VersionProtocolo));
    }
}
