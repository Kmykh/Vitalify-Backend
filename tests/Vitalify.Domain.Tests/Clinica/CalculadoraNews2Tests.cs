using Vitalify.Domain.Clinica;

namespace Vitalify.Domain.Tests.Clinica;

/// <summary>Tablas de NEWS2 (Royal College of Physicians, 2017), en cada límite.</summary>
public class CalculadoraNews2Tests
{
    [Theory]
    [InlineData(8, 3)] [InlineData(9, 1)] [InlineData(11, 1)] [InlineData(12, 0)] [InlineData(20, 0)]
    [InlineData(21, 2)] [InlineData(24, 2)] [InlineData(25, 3)]
    public void Frecuencia_respiratoria(int rpm, int puntos) => Assert.Equal(puntos, CalculadoraNews2.Fr(rpm));

    [Theory]
    [InlineData(91, 3)] [InlineData(92, 2)] [InlineData(93, 2)] [InlineData(94, 1)] [InlineData(95, 1)] [InlineData(96, 0)] [InlineData(100, 0)]
    public void Spo2_escala_1(int spo2, int puntos) => Assert.Equal(puntos, CalculadoraNews2.Spo2(spo2, EscalaSpo2.Escala1, conOxigeno: false));

    [Theory]
    [InlineData(83, false, 3)] [InlineData(84, false, 2)] [InlineData(85, false, 2)] [InlineData(86, false, 1)] [InlineData(87, false, 1)]
    [InlineData(88, false, 0)] [InlineData(92, false, 0)] [InlineData(97, false, 0)]
    [InlineData(93, true, 1)] [InlineData(94, true, 1)] [InlineData(95, true, 2)] [InlineData(96, true, 2)] [InlineData(97, true, 3)]
    public void Spo2_escala_2(int spo2, bool conOxigeno, int puntos) =>
        Assert.Equal(puntos, CalculadoraNews2.Spo2(spo2, EscalaSpo2.Escala2, conOxigeno));

    [Theory]
    [InlineData(90, 3)] [InlineData(91, 2)] [InlineData(100, 2)] [InlineData(101, 1)] [InlineData(110, 1)]
    [InlineData(111, 0)] [InlineData(219, 0)] [InlineData(220, 3)]
    public void Presion_sistolica(int pas, int puntos) => Assert.Equal(puntos, CalculadoraNews2.Pas(pas));

    [Theory]
    [InlineData(40, 3)] [InlineData(41, 1)] [InlineData(50, 1)] [InlineData(51, 0)] [InlineData(90, 0)]
    [InlineData(91, 1)] [InlineData(110, 1)] [InlineData(111, 2)] [InlineData(130, 2)] [InlineData(131, 3)]
    public void Frecuencia_cardiaca(int fc, int puntos) => Assert.Equal(puntos, CalculadoraNews2.Fc(fc));

    [Theory]
    [InlineData("35.0", 3)] [InlineData("35.1", 1)] [InlineData("36.0", 1)] [InlineData("36.1", 0)] [InlineData("38.0", 0)]
    [InlineData("38.1", 1)] [InlineData("39.0", 1)] [InlineData("39.1", 2)]
    public void Temperatura(string celsius, int puntos) =>
        Assert.Equal(puntos, CalculadoraNews2.Temperatura(decimal.Parse(celsius, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Paciente_estable_completo_tiene_riesgo_bajo()
    {
        var r = CalculadoraNews2.Calcular(new(Fr: 16, Spo2: 97, OxigenoSuplementario: false, Pas: 120, Fc: 75, Conciencia: NivelConciencia.Alerta, Temperatura: 36.8m));

        Assert.Equal((0, NivelRiesgoNews2.Bajo, true), (r.Total, r.Nivel, r.Completo));
    }

    [Fact]
    public void Un_parametro_en_tres_eleva_a_bajo_medio_aunque_el_total_sea_bajo()
    {
        var r = CalculadoraNews2.Calcular(new(Fr: 16, Spo2: 97, OxigenoSuplementario: false, Pas: 120, Fc: 75, Conciencia: NivelConciencia.ConfusionNueva, Temperatura: 36.8m));

        Assert.Equal((3, NivelRiesgoNews2.BajoMedio), (r.Total, r.Nivel));
        Assert.True(r.AlgunParametroEnTres);
    }

    [Theory]
    [InlineData(115, 93, NivelRiesgoNews2.Medio)]  // FC 2 + SpO2 2 + FR 2 = 6
    [InlineData(135, 91, NivelRiesgoNews2.Alto)]   // FC 3 + SpO2 3 + FR 2 = 8
    public void El_total_clasifica_el_riesgo_medio_y_alto(int fc, int spo2, NivelRiesgoNews2 nivel)
    {
        var r = CalculadoraNews2.Calcular(new(Fr: 22, Spo2: spo2, OxigenoSuplementario: false, Pas: 120, Fc: fc, Conciencia: NivelConciencia.Alerta, Temperatura: 37.0m));

        Assert.Equal(nivel, r.Nivel);
    }

    [Fact]
    public void El_oxigeno_suplementario_suma_dos()
    {
        var r = CalculadoraNews2.Calcular(new(Spo2: 97, OxigenoSuplementario: true));

        Assert.Equal(2, r.Puntos[ParametroClinico.OxigenoSuplementario]);
    }

    [Fact]
    public void Con_solo_los_datos_del_wearable_el_puntaje_es_parcial_y_dice_que_falta()
    {
        // FC, SpO2 y temperatura: lo que mide el ESP32 de Vitalify.
        var r = CalculadoraNews2.Calcular(new(Spo2: 93, Fc: 112, Temperatura: 38.3m));

        Assert.Equal(2 + 2 + 1, r.Total);
        Assert.False(r.Completo);
        Assert.Equal(
            [ParametroClinico.Fr, ParametroClinico.OxigenoSuplementario, ParametroClinico.Pas, ParametroClinico.Conciencia],
            r.Faltantes);
        Assert.Equal(NivelRiesgoNews2.Medio, r.Nivel);
    }
}
