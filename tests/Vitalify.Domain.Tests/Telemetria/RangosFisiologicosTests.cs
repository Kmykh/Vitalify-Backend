using Vitalify.Domain.Comun;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Domain.Tests.Telemetria;

public class RangosFisiologicosTests
{
    private static readonly RangosFisiologicos Rangos = RangosFisiologicos.PorDefecto;

    [Theory]
    // Variable, justo debajo del mínimo, mínimo, máximo, justo encima del máximo.
    [InlineData(VariableSigno.Fc, "19.9", "20", "250", "250.1")]
    [InlineData(VariableSigno.Fr, "3.9", "4", "60", "60.1")]
    [InlineData(VariableSigno.Spo2, "49.9", "50", "100", "100.1")]
    [InlineData(VariableSigno.Temperatura, "29.9", "30.0", "43.0", "43.1")]
    [InlineData(VariableSigno.Pas, "49.9", "50", "260", "260.1")]
    [InlineData(VariableSigno.Pad, "19.9", "20", "160", "160.1")]
    public void Cada_limite_es_inclusivo_y_lo_de_afuera_se_rechaza(VariableSigno variable, string debajo, string minimo, string maximo, string encima)
    {
        Assert.False(Rangos.EsValido(variable, decimal.Parse(debajo, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.True(Rangos.EsValido(variable, decimal.Parse(minimo, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.True(Rangos.EsValido(variable, decimal.Parse(maximo, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.False(Rangos.EsValido(variable, decimal.Parse(encima, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Los_valores_por_defecto_son_los_de_la_tabla()
    {
        Assert.Equal((20m, 250m), (Rangos.Fc.Minimo, Rangos.Fc.Maximo));
        Assert.Equal((4m, 60m), (Rangos.Fr.Minimo, Rangos.Fr.Maximo));
        Assert.Equal((50m, 100m), (Rangos.Spo2.Minimo, Rangos.Spo2.Maximo));
        Assert.Equal((30.0m, 43.0m), (Rangos.Temperatura.Minimo, Rangos.Temperatura.Maximo));
        Assert.Equal((50m, 260m), (Rangos.Pas.Minimo, Rangos.Pas.Maximo));
        Assert.Equal((20m, 160m), (Rangos.Pad.Minimo, Rangos.Pad.Maximo));
    }

    [Fact]
    public void Un_rango_con_minimo_mayor_o_igual_al_maximo_se_rechaza()
    {
        Assert.Throws<ExcepcionDeDominio>(() => new RangosFisiologicos(
            new(100, 100), Rangos.Fr, Rangos.Spo2, Rangos.Temperatura, Rangos.Pas, Rangos.Pad));
    }
}
