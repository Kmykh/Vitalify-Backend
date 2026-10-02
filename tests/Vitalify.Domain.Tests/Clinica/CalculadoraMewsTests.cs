using Vitalify.Domain.Clinica;

namespace Vitalify.Domain.Tests.Clinica;

/// <summary>Tablas de MEWS (Subbe et al., 2001), en cada límite.</summary>
public class CalculadoraMewsTests
{
    [Theory]
    [InlineData(70, 3)] [InlineData(71, 2)] [InlineData(80, 2)] [InlineData(81, 1)] [InlineData(100, 1)]
    [InlineData(101, 0)] [InlineData(199, 0)] [InlineData(200, 2)]
    public void Presion_sistolica(int pas, int puntos) => Assert.Equal(puntos, CalculadoraMews.Pas(pas));

    [Theory]
    [InlineData(40, 2)] [InlineData(41, 1)] [InlineData(50, 1)] [InlineData(51, 0)] [InlineData(100, 0)]
    [InlineData(101, 1)] [InlineData(110, 1)] [InlineData(111, 2)] [InlineData(129, 2)] [InlineData(130, 3)]
    public void Frecuencia_cardiaca(int fc, int puntos) => Assert.Equal(puntos, CalculadoraMews.Fc(fc));

    [Theory]
    [InlineData(8, 2)] [InlineData(9, 0)] [InlineData(14, 0)] [InlineData(15, 1)] [InlineData(20, 1)]
    [InlineData(21, 2)] [InlineData(29, 2)] [InlineData(30, 3)]
    public void Frecuencia_respiratoria(int fr, int puntos) => Assert.Equal(puntos, CalculadoraMews.Fr(fr));

    [Theory]
    [InlineData("34.9", 2)] [InlineData("35.0", 0)] [InlineData("38.4", 0)] [InlineData("38.5", 2)]
    public void Temperatura(string celsius, int puntos) =>
        Assert.Equal(puntos, CalculadoraMews.Temperatura(decimal.Parse(celsius, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData(NivelConciencia.Alerta, 0)] [InlineData(NivelConciencia.ConfusionNueva, 1)] [InlineData(NivelConciencia.RespondeVoz, 1)]
    [InlineData(NivelConciencia.RespondeDolor, 2)] [InlineData(NivelConciencia.NoResponde, 3)]
    public void Conciencia_AVPU(NivelConciencia nivel, int puntos) => Assert.Equal(puntos, CalculadoraMews.Conciencia(nivel));

    [Theory]
    [InlineData(75, 16, NivelRiesgoMews.Bajo)]   // FR 1
    [InlineData(105, 16, NivelRiesgoMews.Medio)] // FC 1 + FR 1 + PAS 1 (95) = 3
    [InlineData(125, 22, NivelRiesgoMews.Alto)]  // FC 2 + FR 2 + PAS 1 = 5
    public void El_total_clasifica_el_riesgo(int fc, int fr, NivelRiesgoMews nivel)
    {
        var pas = nivel == NivelRiesgoMews.Bajo ? 120 : 95;
        var r = CalculadoraMews.Calcular(new(Fr: fr, Pas: pas, Fc: fc, Conciencia: NivelConciencia.Alerta, Temperatura: 37.0m));

        Assert.Equal(nivel, r.Nivel);
        Assert.True(r.Completo);
    }

    [Fact]
    public void Mews_no_usa_SpO2_y_con_los_datos_del_wearable_es_parcial()
    {
        var r = CalculadoraMews.Calcular(new(Spo2: 80, Fc: 75, Temperatura: 36.8m));

        Assert.Equal(0, r.Total);
        Assert.Equal([ParametroClinico.Pas, ParametroClinico.Fr, ParametroClinico.Conciencia], r.Faltantes);
    }
}
