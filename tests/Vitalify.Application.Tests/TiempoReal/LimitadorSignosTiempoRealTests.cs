using Vitalify.Application.Alertas;

namespace Vitalify.Application.Tests.TiempoReal;

/// <summary>El canal en vivo deja pasar cada lectura del wearable (una por segundo) y frena una ráfaga.</summary>
public class LimitadorSignosTiempoRealTests
{
    [Fact]
    public void Deja_pasar_una_lectura_por_segundo_y_frena_las_que_llegan_juntas()
    {
        var limitador = new LimitadorSignosTiempoReal();
        var paciente = Guid.NewGuid();
        var t = new DateTime(2026, 11, 21, 5, 0, 0, DateTimeKind.Utc);

        Assert.True(limitador.Permitir(paciente, t));
        Assert.False(limitador.Permitir(paciente, t.AddMilliseconds(5))); // el búfer que se vacía al volver la red
        Assert.False(limitador.Permitir(paciente, t.AddMilliseconds(300)));
        Assert.True(limitador.Permitir(paciente, t.AddSeconds(1)));
        Assert.True(limitador.Permitir(paciente, t.AddSeconds(2)));
        Assert.True(limitador.Permitir(Guid.NewGuid(), t.AddSeconds(2))); // cada paciente tiene su propio límite
    }
}
