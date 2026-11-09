using Vitalify.Domain.Alertas;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Nnn;

namespace Vitalify.Domain.Tests.Fase8;

/// <summary>
/// Escenarios clínicos de validación del motor NNN (capítulo de validación de la tesis). Cada uno siembra los signos,
/// las alertas o la toma manual y comprueba las sugerencias exactas.
/// </summary>
public sealed class EscenariosClinicosNnnTests
{
    private static readonly ParametrosClinicos N = PacienteNnn.Normales;

    private static IReadOnlySet<string> Diagnosticos(IReadOnlyList<SugerenciaNnn> s) => s.Select(x => x.DiagnosticoCodigo).ToHashSet();

    private static IReadOnlySet<string> Intervenciones(IReadOnlyList<SugerenciaNnn> s) => s.SelectMany(x => x.Intervenciones).ToHashSet();

    [Fact]
    public void E1_insuficiencia_respiratoria()
    {
        var s = PacienteNnn.Sugerir(N with { Fr = 26, Spo2 = 90 });

        Assert.Equal(new HashSet<string> { "00032", "00030" }, Diagnosticos(s));
        Assert.Equal(new HashSet<string> { "3350", "3320", "3140", "7710" }, Intervenciones(s));
    }

    [Fact]
    public void E2_fiebre_con_taquicardia()
    {
        var parametros = N with { Temperatura = 38.9m, Fc = 118, Fr = 22, Pas = 112 };
        var s = PacienteNnn.Sugerir(parametros);

        Assert.Equal(5, PacienteNnn.Evaluar(parametros).News2Total); // FR 22 (2) + FC 118 (2) + 38,9 °C (1)
        Assert.Equal(new HashSet<string> { "00007", "00240", "00004", "00032" }, Diagnosticos(s));
        Assert.All(s, x => Assert.Contains("7710", x.Intervenciones));
        Assert.Contains("posible respuesta inflamatoria: avise al médico", PacienteNnn.Unica(s, "00004").Motivo);
    }

    [Fact]
    public void E3_hipotension_grave()
    {
        var s = PacienteNnn.Sugerir(N with { Pas = 88, Fc = 135 });

        Assert.Equal(new HashSet<string> { "00029", "00028" }, Diagnosticos(s));
        Assert.Superset(new HashSet<string> { "4250", "4120", "7710" }, Intervenciones(s).ToHashSet());
        Assert.Equal(["4250", "4120", "7710"], PacienteNnn.Unica(s, "00028").Intervenciones);
    }

    [Fact]
    public void E4_confusion_nueva()
    {
        var s = PacienteNnn.Sugerir(N with { Conciencia = NivelConciencia.ConfusionNueva });

        Assert.Equal(new HashSet<string> { "00128", "00303" }, Diagnosticos(s));
    }

    [Fact]
    public void E5_caida_detectada_por_el_wearable()
    {
        var s = PacienteNnn.Sugerir(alertas: [PacienteNnn.Alerta(TipoAlerta.Caida)]);

        var caidas = Assert.Single(s);
        Assert.Equal("00303", caidas.DiagnosticoCodigo);
        Assert.Equal(["6490"], caidas.Intervenciones);
    }

    [Fact]
    public void E6_dolor_7_tras_cirugia()
    {
        var s = Assert.Single(PacienteNnn.Sugerir(observacion: PacienteNnn.ConDolor(7)));

        Assert.Equal("00132", s.DiagnosticoCodigo);
        Assert.Equal(["1410"], s.Intervenciones);
    }

    [Fact]
    public void E7_adulto_mayor_estable_sin_sugerencias()
    {
        var estable = N with { Fr = 18, Spo2 = 96, Pas = 135, Fc = 72, Temperatura = 36.4m };

        Assert.Equal(0, PacienteNnn.Evaluar(estable).News2Total);
        Assert.Empty(PacienteNnn.Sugerir(estable));
    }

    [Fact]
    public void E8_La_Oroya_con_escala_2_de_SpO2_no_sugiere_deterioro_del_intercambio_de_gases()
    {
        // SpO2 89 % con escala 2 (objetivo 88–92 %, sin oxígeno) suma 0 puntos: el sesgo por altitud no dispara 00030.
        var parametros = N with { Spo2 = 89 };

        Assert.Equal(0, PacienteNnn.Evaluar(parametros, EscalaSpo2.Escala2).News2Total);
        Assert.DoesNotContain(PacienteNnn.Sugerir(parametros, EscalaSpo2.Escala2), x => x.DiagnosticoCodigo == "00030");
        Assert.Contains(PacienteNnn.Sugerir(parametros, EscalaSpo2.Escala1), x => x.DiagnosticoCodigo == "00030"); // con escala 1 sí
    }
}
