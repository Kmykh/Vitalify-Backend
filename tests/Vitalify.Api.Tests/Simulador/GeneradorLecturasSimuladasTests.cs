using Vitalify.Api.Entrada.Simulador;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Api.Tests.Simulador;

public class GeneradorLecturasSimuladasTests
{
    private static readonly DateTime Inicio = new(2026, 9, 30, 14, 0, 0, DateTimeKind.Utc);

    private static OpcionesSimulador Opciones(EscenarioSimulacion escenario, int semilla = 7) => new()
    {
        Semilla = semilla,
        Escenarios = new Dictionary<string, EscenarioSimulacion> { ["ESP32-001"] = escenario },
    };

    /// <summary>Una lectura cada 10 s, como el simulador por defecto.</summary>
    private static List<LecturaTelemetria> Generar(OpcionesSimulador opciones, int cantidad, string codigo = "ESP32-001")
    {
        var generador = new GeneradorLecturasSimuladas(opciones);
        return Enumerable.Range(0, cantidad).Select(i => generador.Generar(codigo, Inicio.AddSeconds(10 * i))).ToList();
    }

    [Theory]
    [InlineData(EscenarioSimulacion.Estable)]
    [InlineData(EscenarioSimulacion.Deterioro)]
    [InlineData(EscenarioSimulacion.Caida)]
    [InlineData(EscenarioSimulacion.SensorDefectuoso)]
    public void Cada_escenario_es_determinista_con_la_misma_semilla(EscenarioSimulacion escenario)
    {
        var primera = Generar(Opciones(escenario), 60);
        var segunda = Generar(Opciones(escenario), 60);

        Assert.Equal(primera, segunda);
    }

    [Fact]
    public void Otra_semilla_produce_otra_secuencia()
    {
        Assert.NotEqual(Generar(Opciones(EscenarioSimulacion.Estable, 1), 20), Generar(Opciones(EscenarioSimulacion.Estable, 2), 20));
    }

    [Fact]
    public void Estable_genera_valores_normales_con_seq_creciente_y_origen_simulador()
    {
        var lecturas = Generar(Opciones(EscenarioSimulacion.Estable), 50);

        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), lecturas.Select(l => l.Seq));
        Assert.All(lecturas, l =>
        {
            Assert.Equal(OrigenLectura.Simulador, l.Origen);
            Assert.InRange(l.Signos.Fc!.Value, 70, 85);
            Assert.InRange(l.Signos.Fr!.Value, 14, 18);
            Assert.InRange(l.Signos.Spo2!.Value, 96, 99);
            Assert.InRange(l.Signos.Temperatura!.Value, 36.5m, 37.2m);
            Assert.InRange(l.Signos.Pas!.Value, 110, 125);
            Assert.False(l.Caida);
            Assert.Empty(EvaluadorSignos.Evaluar(l.Signos, RangosFisiologicos.PorDefecto).Descartes);
        });
    }

    [Fact]
    public void Deterioro_empeora_gradualmente_durante_los_minutos_configurados()
    {
        var lecturas = Generar(Opciones(EscenarioSimulacion.Deterioro), 181);
        var (inicio, fin) = (lecturas[0].Signos, lecturas[180].Signos);

        Assert.InRange(inicio.Fc!.Value, 70, 85);
        Assert.InRange(fin.Fc!.Value, 115, 130);
        Assert.InRange(fin.Fr!.Value, 26, 30);
        Assert.InRange(fin.Spo2!.Value, 86, 89);
        Assert.InRange(fin.Pas!.Value, 80, 95);
        Assert.True(fin.Temperatura > inicio.Temperatura);
        Assert.All(lecturas, l => Assert.Empty(EvaluadorSignos.Evaluar(l.Signos, RangosFisiologicos.PorDefecto).Descartes));
    }

    [Fact]
    public void Caida_marca_una_caida_cada_tantas_lecturas()
    {
        var lecturas = Generar(Opciones(EscenarioSimulacion.Caida), 54);

        Assert.Equal([18L, 36L, 54L], lecturas.Where(l => l.Caida).Select(l => l.Seq));
    }

    [Fact]
    public void SensorDefectuoso_mezcla_valores_imposibles_presion_incompleta_y_huecos_de_temperatura()
    {
        var lecturas = Generar(Opciones(EscenarioSimulacion.SensorDefectuoso), 24);

        Assert.All(lecturas.Where(l => l.Seq % 4 == 0), l => Assert.Equal(400, l.Signos.Fc));
        Assert.All(lecturas.Where(l => l.Seq % 5 == 0), l => Assert.Null(l.Signos.Pad));
        Assert.All(lecturas.Where(l => l.Seq is >= 6 and < 18), l => Assert.Null(l.Signos.Temperatura));
        Assert.NotNull(lecturas.Single(l => l.Seq == 4).Signos.Temperatura); // fuera del hueco sí llega

        // El hueco de temperatura dura más que la vigencia de 90 s.
        var sinTemperatura = lecturas.Where(l => l.Signos.Temperatura is null).ToList();
        Assert.True(sinTemperatura[^1].MedidoEn - sinTemperatura[0].MedidoEn > TimeSpan.FromSeconds(90));
    }

    [Fact]
    public void Cada_dispositivo_lleva_su_propio_seq_y_un_escenario_por_defecto_estable()
    {
        var generador = new GeneradorLecturasSimuladas(Opciones(EscenarioSimulacion.SensorDefectuoso));

        generador.Generar("ESP32-001", Inicio);
        generador.Generar("ESP32-001", Inicio.AddSeconds(10));
        var otro = generador.Generar("ESP32-009", Inicio.AddSeconds(10));

        Assert.Equal(1, otro.Seq);
        Assert.Equal(EscenarioSimulacion.Estable, Opciones(EscenarioSimulacion.Deterioro).EscenarioDe("ESP32-009"));
    }
}
