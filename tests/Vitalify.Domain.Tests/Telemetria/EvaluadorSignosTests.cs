using Vitalify.Domain.Telemetria;

namespace Vitalify.Domain.Tests.Telemetria;

public class EvaluadorSignosTests
{
    private static EvaluacionSignos Evaluar(SignosMedidos medidos) => EvaluadorSignos.Evaluar(medidos, RangosFisiologicos.PorDefecto);

    [Fact]
    public void Una_lectura_completa_y_valida_no_tiene_descartes()
    {
        var evaluacion = Evaluar(new(Fc: 82, Fr: 16, Spo2: 97, Temperatura: 36.84m, Pas: 118, Pad: 76, Bateria: 87));

        Assert.Empty(evaluacion.Descartes);
        Assert.Equal(new SignosValidos(82, 16, 97, 36.8m, 118, 76, 87), evaluacion.Validos);
    }

    [Fact]
    public void Una_variable_fuera_de_rango_se_descarta_sola_y_las_demas_se_conservan()
    {
        var evaluacion = Evaluar(new(Fc: 400, Fr: 16, Spo2: 97));

        var descarte = Assert.Single(evaluacion.Descartes);
        Assert.Equal((TipoIncidencia.FueraDeRango, VariableSigno.Fc, 400m, false),
            (descarte.Tipo, descarte.Variable, descarte.Valor, descarte.RequiereNuevaLectura));
        Assert.Contains("400", descarte.Detalle);
        Assert.Null(evaluacion.Validos.Fc);
        Assert.Equal((16, 97), (evaluacion.Validos.Fr, evaluacion.Validos.Spo2));
        Assert.True(evaluacion.Validos.TieneAlgunSigno);
    }

    [Theory]
    [InlineData(118, null, "diastólica")]
    [InlineData(null, 76, "sistólica")]
    public void La_presion_incompleta_descarta_las_dos_y_pide_otra_lectura(int? pas, int? pad, string falta)
    {
        var evaluacion = Evaluar(new(Fc: 80, Pas: pas, Pad: pad));

        var descarte = Assert.Single(evaluacion.Descartes);
        Assert.Equal(TipoIncidencia.PresionIncompleta, descarte.Tipo);
        Assert.True(descarte.RequiereNuevaLectura);
        Assert.Contains(falta, descarte.Detalle);
        Assert.True(evaluacion.PresionIncompleta);
        Assert.Equal((null, null), (evaluacion.Validos.Pas, evaluacion.Validos.Pad));
        Assert.Equal(80, evaluacion.Validos.Fc);
    }

    [Theory]
    [InlineData(80, 80)]
    [InlineData(70, 90)]
    public void La_presion_con_PAS_menor_o_igual_a_PAD_es_incoherente(int pas, int pad)
    {
        var evaluacion = Evaluar(new(Pas: pas, Pad: pad));

        Assert.True(evaluacion.PresionIncompleta);
        Assert.Contains("incoherente", Assert.Single(evaluacion.Descartes).Detalle);
        Assert.False(evaluacion.Validos.TieneAlgunSigno);
    }

    [Fact]
    public void Si_PAS_o_PAD_estan_fuera_de_rango_se_descarta_el_par_sin_pedir_otra_lectura()
    {
        var evaluacion = Evaluar(new(Pas: 300, Pad: 80));

        var descarte = Assert.Single(evaluacion.Descartes);
        Assert.Equal((TipoIncidencia.FueraDeRango, VariableSigno.Pas, false), (descarte.Tipo, descarte.Variable, descarte.RequiereNuevaLectura));
        Assert.Equal((null, null), (evaluacion.Validos.Pas, evaluacion.Validos.Pad));
    }

    [Fact]
    public void Sin_ningun_signo_valido_la_lectura_no_tiene_signos_aunque_haya_bateria()
    {
        var evaluacion = Evaluar(new(Fc: 400, Bateria: 50));

        Assert.False(evaluacion.Validos.TieneAlgunSigno);
        Assert.Equal(50, evaluacion.Validos.Bateria);
    }

    [Fact]
    public void Una_bateria_imposible_se_descarta()
    {
        var evaluacion = Evaluar(new(Fc: 80, Bateria: 130));

        Assert.Equal(VariableSigno.Bateria, Assert.Single(evaluacion.Descartes).Variable);
        Assert.Null(evaluacion.Validos.Bateria);
    }

    [Fact]
    public void Los_valores_se_redondean_enteros_y_la_temperatura_a_un_decimal()
    {
        var evaluacion = Evaluar(new(Fc: 82.5m, Temperatura: 37.25m));

        Assert.Equal((83, 37.3m), (evaluacion.Validos.Fc, evaluacion.Validos.Temperatura));
    }
}
