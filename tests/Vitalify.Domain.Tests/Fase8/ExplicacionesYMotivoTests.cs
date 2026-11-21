using Vitalify.Domain.Alertas;
using Vitalify.Domain.Clinica;

namespace Vitalify.Domain.Tests.Fase8;

/// <summary>
/// Lo que lee la enfermera novata: el "por qué" de cada sugerencia NNN en lenguaje de piso y el parámetro que más sube el
/// NEWS2 (para la lista y la alerta).
/// </summary>
public class ExplicacionesYMotivoTests
{
    [Fact]
    public void La_FR_alta_se_explica_con_el_valor_y_el_rango_normal_sin_puntos_NEWS2()
    {
        var s = PacienteNnn.Sugerir(PacienteNnn.Normales with { Fr = 26 }).Single(x => x.DiagnosticoCodigo == "00032");

        Assert.Equal("FR 26 rpm: 3 puntos NEWS2. NEWS2 3 con un parámetro en 3: avise al médico", s.Motivo); // el técnico se conserva
        Assert.Equal("Respira 26 veces por minuto; lo normal es de 12 a 20. Su NEWS2 es 3 y un signo suma 3 puntos: avisa al médico.", s.Explicacion);
        Assert.DoesNotContain("puntos NEWS2", s.Explicacion!.Replace("suma 3 puntos", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void La_SpO2_baja_dice_lo_esperado_segun_la_escala()
    {
        var escala1 = PacienteNnn.Sugerir(PacienteNnn.Normales with { Spo2 = 92 }).Single(x => x.DiagnosticoCodigo == "00030");
        Assert.StartsWith("Su saturación de oxígeno es 92 % (lo esperado es 96 % o más).", escala1.Explicacion, StringComparison.Ordinal);

        var conOxigeno = PacienteNnn.Sugerir(PacienteNnn.Normales with { Spo2 = 95, OxigenoSuplementario = true }, EscalaSpo2.Escala2)
            .Single(x => x.DiagnosticoCodigo == "00030");
        Assert.StartsWith("Con oxígeno, su saturación es 95 %, por encima de la meta de 88 a 92 %", conOxigeno.Explicacion, StringComparison.Ordinal);
    }

    [Fact]
    public void La_fiebre_la_caida_y_la_conciencia_se_explican_sin_codigos()
    {
        var fiebre = PacienteNnn.Sugerir(PacienteNnn.Normales with { Temperatura = 38.6m }).Single(x => x.DiagnosticoCodigo == "00007");
        Assert.Equal("Tiene fiebre: 38,6 °C (lo normal es de 36,1 a 38,0 °C).", fiebre.Explicacion);

        var caida = PacienteNnn.Sugerir(alertas: [PacienteNnn.Alerta(TipoAlerta.Caida)]).Single(x => x.DiagnosticoCodigo == "00303");
        Assert.Equal("El wearable detectó una caída.", caida.Explicacion);

        var confusa = PacienteNnn.Sugerir(PacienteNnn.Normales with { Conciencia = NivelConciencia.ConfusionNueva });
        Assert.StartsWith("Tiene confusión nueva", confusa.Single(x => x.DiagnosticoCodigo == "00128").Explicacion, StringComparison.Ordinal);
        Assert.Contains("aumenta el riesgo de caerse", confusa.Single(x => x.DiagnosticoCodigo == "00303").Explicacion, StringComparison.Ordinal);
    }

    [Fact]
    public void El_motivo_principal_es_el_parametro_que_mas_suma_y_a_igual_puntaje_el_primero_de_la_tabla()
    {
        // FR 24 (+2), SpO2 93 (+2), FC 112 (+2), T 38,3 (+1): empatan FR, SpO2 y FC; gana la FR
        var e = PacienteNnn.Evaluar(PacienteNnn.Normales with { Fr = 24, Spo2 = 93, Fc = 112, Temperatura = 38.3m });
        Assert.Equal(new MotivoPuntaje(ParametroClinico.Fr, 2, "24 rpm"), MotivoPuntaje.De(e));

        // Una FC de 135 (+3) gana aunque la FR también sume
        var fc = PacienteNnn.Evaluar(PacienteNnn.Normales with { Fr = 22, Fc = 135 });
        Assert.Equal(new MotivoPuntaje(ParametroClinico.Fc, 3, "135 lpm"), MotivoPuntaje.De(fc));

        Assert.Equal("38.4 °C", MotivoPuntaje.De(PacienteNnn.Evaluar(PacienteNnn.Normales with { Temperatura = 38.4m }))!.Valor);
        Assert.Null(MotivoPuntaje.De(PacienteNnn.Evaluar(PacienteNnn.Normales)));
    }
}
