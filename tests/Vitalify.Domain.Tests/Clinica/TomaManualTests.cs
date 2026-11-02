using Vitalify.Domain.Clinica;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Domain.Tests.Clinica;

public class RespuestasConcienciaTests
{
    [Theory]
    [InlineData(true, false, null, null, NivelConciencia.Alerta)]
    [InlineData(true, true, null, null, NivelConciencia.ConfusionNueva)]
    [InlineData(false, null, true, null, NivelConciencia.RespondeVoz)]
    [InlineData(false, null, false, true, NivelConciencia.RespondeDolor)]
    [InlineData(false, null, false, false, NivelConciencia.NoResponde)]
    public void Cada_rama_de_la_cascada_da_su_nivel(bool despierto, bool? confusion, bool? voz, bool? dolor, NivelConciencia nivel) =>
        Assert.Equal(nivel, new RespuestasConciencia(despierto, confusion, voz, dolor).Nivel);

    [Fact]
    public void Con_confusion_basal_que_no_empeora_es_alerta()
    {
        // La pregunta cambia ("¿más confundido que de costumbre?") pero el cálculo es el mismo: solo cuenta lo nuevo.
        Assert.Equal(NivelConciencia.Alerta, new RespuestasConciencia(DespiertoCoherente: true, ConfusionNueva: false).Nivel);
        Assert.Equal(NivelConciencia.ConfusionNueva, new RespuestasConciencia(DespiertoCoherente: true, ConfusionNueva: true).Nivel);
    }

    [Fact]
    public void Las_respuestas_que_sobran_no_cambian_el_nivel()
    {
        Assert.Equal(NivelConciencia.Alerta, new RespuestasConciencia(true, false, RespondeVoz: false, RespondeDolor: false).Nivel);
    }

    [Theory]
    [InlineData(null, null, null, null, "despiertoCoherente")]
    [InlineData(true, null, null, null, "confusionNueva")]
    [InlineData(false, null, null, null, "respondeVoz")]
    [InlineData(false, null, false, null, "respondeDolor")]
    public void Si_falta_una_respuesta_de_la_cascada_no_se_calcula(bool? despierto, bool? confusion, bool? voz, bool? dolor, string pendiente)
    {
        var respuestas = new RespuestasConciencia(despierto, confusion, voz, dolor);

        Assert.Equal(pendiente, respuestas.PreguntaPendiente);
        Assert.Throws<ExcepcionDeDominio>(() => respuestas.Nivel);
    }
}

public class TomaPresionTests
{
    private static TomaPresion Tomar(params (int Pas, int Pad)[] lecturas) =>
        TomaPresion.Crear(lecturas.Select(l => new LecturaPresion(l.Pas, l.Pad, null)).ToList(), null, null, null, null, RangosFisiologicos.PorDefecto);

    [Fact]
    public void Una_lectura_se_registra_tal_cual() => Assert.Equal((118, 76), (Tomar((118, 76)).Pas, Tomar((118, 76)).Pad));

    [Fact]
    public void Dos_lecturas_se_promedian_redondeando_el_medio_hacia_arriba()
    {
        var toma = Tomar((134, 86), (131, 83));

        Assert.Equal((133, 85), (toma.Pas, toma.Pad));
    }

    [Fact]
    public void Tres_lecturas_se_promedian_todas()
    {
        var toma = Tomar((140, 80), (128, 80), (131, 82));

        Assert.Equal((133, 81), (toma.Pas, toma.Pad));
    }

    [Theory]
    [InlineData(130, 80, 120, 80, false)]
    [InlineData(130, 80, 119, 80, true)]
    [InlineData(130, 90, 130, 80, false)]
    [InlineData(130, 91, 130, 80, true)]
    public void Dos_lecturas_que_difieren_en_mas_de_10_mmHg_piden_una_tercera(int pas1, int pad1, int pas2, int pad2, bool tercera)
    {
        var lecturas = new[] { new LecturaPresion(pas1, pad1, null), new LecturaPresion(pas2, pad2, null) };

        Assert.Equal(tercera, TomaPresion.RequiereTerceraLectura(lecturas));
        if (tercera)
        {
            Assert.Throws<ExcepcionDeDominio>(() => Tomar((pas1, pad1), (pas2, pad2)));
        }
    }

    [Fact]
    public void Se_rechazan_cero_o_cuatro_lecturas_y_una_sistolica_menor_o_igual_que_la_diastolica()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Tomar());
        Assert.Throws<ExcepcionDeDominio>(() => Tomar((120, 80), (121, 80), (122, 80), (123, 80)));
        Assert.Throws<ExcepcionDeDominio>(() => Tomar((80, 80)));
        Assert.Throws<ExcepcionDeDominio>(() => Tomar((300, 80)));
    }

    [Fact]
    public void El_checklist_solo_esta_completo_con_las_cinco_condiciones()
    {
        Assert.True(new ChecklistPresion(true, true, true, true, true).Completo);
        Assert.False(new ChecklistPresion(true, true, false, true, true).Completo);
    }
}

public class OxigenoYContextoTests
{
    [Fact]
    public void Si_recibe_oxigeno_hace_falta_el_dispositivo()
    {
        Assert.Throws<ExcepcionDeDominio>(() => OxigenoAdministrado.Crear(true, null, 2, null, null));
        Assert.True(OxigenoAdministrado.Crear(true, DispositivoOxigeno.CanulaNasal, 2, null, false).Recibe);
    }

    [Fact]
    public void Si_no_recibe_oxigeno_no_hay_dispositivo_flujo_ni_fio2()
    {
        Assert.Throws<ExcepcionDeDominio>(() => OxigenoAdministrado.Crear(false, DispositivoOxigeno.CanulaNasal, null, null, null));
        Assert.Throws<ExcepcionDeDominio>(() => OxigenoAdministrado.Crear(false, null, 2, null, null));
        Assert.False(OxigenoAdministrado.Crear(false, null, null, null, true).Recibe);
    }

    [Theory]
    [InlineData(DispositivoOxigeno.Venturi, 28, true)]
    [InlineData(DispositivoOxigeno.AltoFlujo, 60, true)]
    [InlineData(DispositivoOxigeno.CanulaNasal, 28, false)]
    [InlineData(DispositivoOxigeno.Venturi, 20, false)]
    [InlineData(DispositivoOxigeno.Venturi, 101, false)]
    public void La_fio2_solo_con_venturi_o_alto_flujo_y_entre_21_y_100(DispositivoOxigeno dispositivo, int fio2, bool valida)
    {
        var crear = () => OxigenoAdministrado.Crear(true, dispositivo, 8, fio2, null);

        if (valida)
        {
            Assert.Equal(fio2, crear().Fio2);
        }
        else
        {
            Assert.Throws<ExcepcionDeDominio>(crear);
        }
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(61)]
    public void El_flujo_va_de_medio_litro_a_60(double flujo) =>
        Assert.Throws<ExcepcionDeDominio>(() => OxigenoAdministrado.Crear(true, DispositivoOxigeno.AltoFlujo, (decimal)flujo, null, null));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void El_dolor_va_de_0_a_10(int dolor, bool valido)
    {
        if (valido)
        {
            Assert.Equal(dolor, ContextoClinico.Crear(dolor, null, null, null).Dolor);
        }
        else
        {
            Assert.Throws<ExcepcionDeDominio>(() => ContextoClinico.Crear(dolor, null, null, null));
        }
    }
}

public class FrecuenciaTomaManualTests
{
    private static readonly DateTime Toma = new(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc);

    private static ResultadoNews2 News2(int total, NivelRiesgoNews2 nivel) =>
        new(total, new Dictionary<ParametroClinico, int>(), [], nivel);

    [Theory]
    [InlineData(0, NivelRiesgoNews2.Bajo, 12)]
    [InlineData(1, NivelRiesgoNews2.Bajo, 4)]
    [InlineData(4, NivelRiesgoNews2.Bajo, 4)]
    [InlineData(3, NivelRiesgoNews2.BajoMedio, 1)]
    [InlineData(4, NivelRiesgoNews2.BajoMedio, 1)]
    [InlineData(5, NivelRiesgoNews2.Medio, 1)]
    [InlineData(6, NivelRiesgoNews2.Medio, 1)]
    [InlineData(7, NivelRiesgoNews2.Alto, 1)]
    [InlineData(12, NivelRiesgoNews2.Alto, 1)]
    public void Proxima_toma_segun_el_puntaje(int total, NivelRiesgoNews2 nivel, int horas) =>
        Assert.Equal(Toma.AddHours(horas), FrecuenciaTomaManual.Proxima(Toma, Toma.AddMinutes(30), News2(total, nivel)));

    [Fact]
    public void Sin_ninguna_toma_manual_esta_pendiente_desde_la_evaluacion()
    {
        var evaluadaEn = Toma.AddMinutes(30);

        Assert.Equal(evaluadaEn, FrecuenciaTomaManual.Proxima(null, evaluadaEn, News2(0, NivelRiesgoNews2.Bajo)));
    }
}

public class ObservacionAsistenteTests
{
    private static readonly DateTime Ahora = new(2026, 11, 2, 14, 0, 0, DateTimeKind.Utc);

    private static ObservacionEnfermeria Registrar(DatosObservacion datos) =>
        ObservacionEnfermeria.Registrar(Guid.NewGuid(), Guid.NewGuid(), Ahora, Ahora, Guid.NewGuid(), datos, RangosFisiologicos.PorDefecto);

    [Fact]
    public void La_presion_del_asistente_guarda_el_promedio_las_lecturas_y_sus_condiciones()
    {
        var toma = TomaPresion.Crear(
            [new LecturaPresion(134, 86, Ahora.AddMinutes(-2)), new LecturaPresion(130, 84, Ahora)],
            new ChecklistPresion(true, true, true, true, false), PosicionPresion.Sentado, BrazoPresion.Izquierdo, MetodoPresion.Automatico,
            RangosFisiologicos.PorDefecto);

        var o = Registrar(new DatosObservacion { Presion = toma, VersionGuia = "2026.11.1" });

        Assert.Equal(((int?)132, (int?)85, 2), (o.Pas, o.Pad, o.LecturasPa!.Count));
        Assert.Equal((PosicionPresion.Sentado, BrazoPresion.Izquierdo, MetodoPresion.Automatico), (o.PosicionPa!.Value, o.BrazoPa!.Value, o.MetodoPa!.Value));
        Assert.False(o.ChecklistPa!.Completo);
        Assert.Equal("2026.11.1", o.VersionGuia);
    }

    [Fact]
    public void No_acepta_la_presion_de_las_dos_formas_a_la_vez()
    {
        var toma = TomaPresion.Crear([new LecturaPresion(120, 80, null)], null, null, null, null, RangosFisiologicos.PorDefecto);

        Assert.Throws<ExcepcionDeDominio>(() => Registrar(new DatosObservacion { Presion = toma, Pas = 120, Pad = 80 }));
    }

    [Fact]
    public void El_oxigeno_del_asistente_gana_al_campo_plano()
    {
        var o = Registrar(new DatosObservacion
        {
            OxigenoSuplementario = false,
            Oxigeno = OxigenoAdministrado.Crear(true, DispositivoOxigeno.Venturi, 6, 28, true),
        });

        Assert.Equal(((bool?)true, (DispositivoOxigeno?)DispositivoOxigeno.Venturi, (int?)28, (bool?)true),
            (o.OxigenoSuplementario, o.DispositivoOxigeno, o.Fio2, o.ObjetivoSpo288a92));
    }

    [Fact]
    public void La_preocupacion_clinica_sola_es_una_observacion_valida()
    {
        Assert.True(Registrar(new DatosObservacion { PreocupacionClinica = true }).PreocupacionClinica);
        Assert.Throws<ExcepcionDeDominio>(() => Registrar(new DatosObservacion { Contexto = ContextoClinico.Crear(3, null, null, null) }));
    }
}

public class EscalaSpo2PorPacienteTests
{
    private static readonly DateTime Ahora = new(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void La_hospitalizacion_empieza_en_escala_1_y_el_medico_la_cambia_con_un_motivo()
    {
        var hospitalizacion = Hospitalizacion.Abrir(Guid.NewGuid(), Guid.NewGuid(), "EPOC exacerbado", Ahora, Guid.NewGuid());
        var medico = Guid.NewGuid();
        Assert.Equal(EscalaSpo2.Escala1, hospitalizacion.EscalaSpo2);

        hospitalizacion.CambiarEscalaSpo2(EscalaSpo2.Escala2, "  Hipercapnia crónica  ", Ahora.AddHours(1), medico);

        Assert.Equal((EscalaSpo2.Escala2, "Hipercapnia crónica", (DateTime?)Ahora.AddHours(1), (Guid?)medico),
            (hospitalizacion.EscalaSpo2, hospitalizacion.MotivoEscalaSpo2, hospitalizacion.EscalaSpo2CambiadaEn, hospitalizacion.EscalaSpo2CambiadaPor));
        Assert.Throws<ExcepcionDeDominio>(() => hospitalizacion.CambiarEscalaSpo2(EscalaSpo2.Escala1, "no", Ahora, medico));

        hospitalizacion.RegistrarEgreso(MotivoEgreso.AltaMedica, null, Ahora.AddDays(1), Guid.NewGuid());
        Assert.Throws<ExcepcionDeDominio>(() => hospitalizacion.CambiarEscalaSpo2(EscalaSpo2.Escala1, "Mejoró", Ahora.AddDays(1), medico));
    }
}
