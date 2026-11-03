using Vitalify.Domain.Comun;
using Vitalify.Domain.Historial;

namespace Vitalify.Domain.Tests.Fase7;

public sealed class RetencionHistorialTests
{
    private static readonly DateTime Ahora = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

    /// <summary>Lima es UTC−5 todo el año (sin horario de verano).</summary>
    private static readonly TimeZoneInfo Lima = TimeZoneInfo.CreateCustomTimeZone("America/Lima", TimeSpan.FromHours(-5), "Lima", "Lima");

    [Fact]
    public void Solo_se_resume_un_episodio_finalizado_cuyo_egreso_cumplio_los_30_dias()
    {
        var politica = PoliticaRetencion.PorDefecto;

        Assert.False(politica.DebeResumirse(null, Ahora)); // activa
        Assert.False(politica.DebeResumirse(Ahora.AddDays(-29), Ahora));
        Assert.False(politica.DebeResumirse(Ahora.AddDays(-30), Ahora));
        Assert.True(politica.DebeResumirse(Ahora.AddDays(-30).AddSeconds(-1), Ahora));
    }

    [Fact]
    public void Las_crudas_se_guardan_al_menos_un_dia() =>
        Assert.Throws<ExcepcionDeDominio>(() => new PoliticaRetencion(TimeSpan.FromHours(23)));

    [Theory]
    [InlineData("2026-11-03T07:59:59Z", "2026-11-03T08:00:00Z")] // 02:59:59 en Lima: hoy a las 03:00 (08:00 UTC)
    [InlineData("2026-11-03T08:00:00Z", "2026-11-04T08:00:00Z")] // justo a las 03:00: la siguiente es mañana
    [InlineData("2026-11-03T15:00:00Z", "2026-11-04T08:00:00Z")]
    [InlineData("2026-12-31T23:00:00Z", "2027-01-01T08:00:00Z")] // 18:00 del 31 en Lima
    public void La_retencion_corre_a_las_3_de_la_manana_de_Lima(string ahora, string esperada)
    {
        var proxima = ProgramaDiario.Proxima(
            DateTime.Parse(ahora, null, System.Globalization.DateTimeStyles.AdjustToUniversal), new TimeOnly(3, 0), Lima);

        Assert.Equal(DateTime.Parse(esperada, null, System.Globalization.DateTimeStyles.AdjustToUniversal), proxima);
        Assert.Equal(DateTimeKind.Utc, proxima.Kind);
    }
}
