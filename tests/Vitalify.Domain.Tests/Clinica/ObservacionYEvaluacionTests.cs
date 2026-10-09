using Vitalify.Domain.Clinica;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Domain.Tests.Clinica;

public class ObservacionYEvaluacionTests
{
    private static readonly DateTime Ahora = new(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc);

    private static ObservacionEnfermeria Observar(int? fr = 18, int? pas = 120, int? pad = 80, decimal? temp = null,
        NivelConciencia? conciencia = NivelConciencia.Alerta, bool? oxigeno = false, DateTime? observadaEn = null) =>
        ObservacionEnfermeria.Registrar(Guid.NewGuid(), Guid.NewGuid(), observadaEn ?? Ahora, Ahora, Guid.NewGuid(),
            fr, pas, pad, temp, conciencia, oxigeno, RangosFisiologicos.PorDefecto);

    [Fact]
    public void Una_observacion_valida_guarda_sus_valores()
    {
        var o = Observar(temp: 37.25m);

        Assert.Equal((18, 120, 80, 37.3m, NivelConciencia.Alerta, false), (o.Fr, o.Pas, o.Pad, o.Temperatura, o.Conciencia, o.OxigenoSuplementario));
    }

    [Fact]
    public void Una_observacion_vacia_se_rechaza()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Observar(fr: null, pas: null, pad: null, conciencia: null, oxigeno: null));
    }

    [Theory]
    [InlineData(120, null)]
    [InlineData(null, 80)]
    [InlineData(80, 80)]
    public void La_presion_necesita_las_dos_y_sistolica_mayor(int? pas, int? pad)
    {
        Assert.Throws<ExcepcionDeDominio>(() => Observar(pas: pas, pad: pad));
    }

    [Fact]
    public void Los_valores_imposibles_y_las_observaciones_futuras_se_rechazan()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Observar(fr: 90));
        Assert.Throws<ExcepcionDeDominio>(() => Observar(observadaEn: Ahora.AddMinutes(1)));
    }

    [Fact]
    public void La_evaluacion_guarda_los_totales_y_recalcula_el_desglose()
    {
        var parametros = new ParametrosClinicos(Fr: 22, Spo2: 93, OxigenoSuplementario: false, Pas: 120, Fc: 115, Conciencia: NivelConciencia.Alerta, Temperatura: 37.0m);

        var e = EvaluacionRiesgo.Calcular(Guid.NewGuid(), Guid.NewGuid(), Ahora, OrigenEvaluacion.Lectura, parametros);

        Assert.Equal((6, NivelRiesgoNews2.Medio, true), (e.News2Total, e.News2Nivel, e.News2Completo));
        Assert.Equal((4, NivelRiesgoMews.Medio, true), (e.MewsTotal, e.MewsNivel, e.MewsCompleto));
        Assert.Equal(parametros, e.Parametros);
        Assert.Equal(2, e.News2().Puntos[ParametroClinico.Fc]);
    }
}
