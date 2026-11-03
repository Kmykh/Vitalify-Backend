using Vitalify.Domain.Clinica;

namespace Vitalify.Domain.Tests.Fase7;

/// <summary>Una evaluación del wearable solo se guarda si cambia algo o si pasaron 5 min desde la última guardada.</summary>
public sealed class EvaluacionesSinCambiosTests
{
    private static readonly DateTime Ahora = new(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Hospitalizacion = Guid.NewGuid();

    private static readonly ParametrosClinicos Estable =
        new(Fr: 16, Spo2: 97, OxigenoSuplementario: false, Pas: 120, Fc: 80, Conciencia: NivelConciencia.Alerta, Temperatura: 36.8m);

    private static EvaluacionRiesgo Evaluar(DateTime en, ParametrosClinicos? parametros = null, DateTime? proximaToma = null)
    {
        var e = EvaluacionRiesgo.Calcular(Hospitalizacion, Guid.NewGuid(), en, OrigenEvaluacion.Lectura, parametros ?? Estable);
        e.FijarProximaTomaManual(proximaToma ?? Ahora.AddHours(12));
        return e;
    }

    [Fact]
    public void La_primera_evaluacion_siempre_aporta() => Assert.True(Evaluar(Ahora).AportaRespectoDe(null));

    [Fact]
    public void Con_los_mismos_puntajes_y_menos_de_5_minutos_no_aporta()
    {
        var anterior = Evaluar(Ahora);

        // Otros valores con los mismos puntos (FC 82 y SpO2 98 siguen en 0): nada que guardar.
        var siguiente = Evaluar(Ahora.AddMinutes(4).AddSeconds(59), Estable with { Fc = 82, Spo2 = 98 });

        Assert.False(siguiente.AportaRespectoDe(anterior));
    }

    [Fact]
    public void A_los_5_minutos_se_guarda_aunque_no_cambie_nada() =>
        Assert.True(Evaluar(Ahora.AddMinutes(5)).AportaRespectoDe(Evaluar(Ahora)));

    [Theory]
    [InlineData("news2")] // SpO2 95: NEWS2 +1, mismo nivel
    [InlineData("nivel")] // FR 25: NEWS2 3 en un parámetro, sube a bajo-medio
    [InlineData("mews")] // PAS 100 no puntúa en NEWS2 (101-110 sí); en MEWS 81-100 suma 1
    [InlineData("completo")] // sin temperatura: los dos puntajes quedan parciales
    public void Un_cambio_de_puntaje_nivel_o_completitud_aporta_al_instante(string cambio)
    {
        var anterior = Evaluar(Ahora);
        var parametros = cambio switch
        {
            "news2" => Estable with { Spo2 = 95 },
            "nivel" => Estable with { Fr = 25 },
            "mews" => Estable with { Pas = 100 },
            _ => Estable with { Temperatura = null },
        };

        var siguiente = Evaluar(Ahora.AddSeconds(10), parametros);

        Assert.True(siguiente.AportaRespectoDe(anterior));
    }

    [Fact]
    public void Un_cambio_de_la_proxima_toma_aporta_porque_de_ella_depende_cuanto_cuenta_cada_toma() =>
        Assert.True(Evaluar(Ahora.AddSeconds(10), proximaToma: Ahora.AddHours(4)).AportaRespectoDe(Evaluar(Ahora)));
}
