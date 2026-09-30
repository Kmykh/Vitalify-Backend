using Vitalify.Domain.Comun;
using Vitalify.Domain.Dispositivos;

namespace Vitalify.Domain.Tests.Dispositivos;

public class DispositivoTests
{
    [Fact]
    public void Registrar_normaliza_el_codigo_y_queda_disponible()
    {
        var dispositivo = Dispositivo.Registrar(" esp32-001 ", "  Pulsera de prueba ");

        Assert.Equal("ESP32-001", dispositivo.Codigo);
        Assert.Equal("Pulsera de prueba", dispositivo.Descripcion);
        Assert.Equal(EstadoDispositivo.Disponible, dispositivo.Estado);
    }

    [Theory]
    [InlineData("ES")]
    [InlineData("ESP32 001")]
    [InlineData("ESP32_001")]
    [InlineData("ESP32/001")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456")]
    public void Rechaza_codigos_que_no_cumplen_el_patron(string codigo)
    {
        Assert.False(Dispositivo.CodigoValido(codigo));
        Assert.Throws<ExcepcionDeDominio>(() => Dispositivo.Registrar(codigo, null));
    }

    public static TheoryData<EstadoDispositivo, string, EstadoDispositivo?> Transiciones => new()
    {
        // Estado inicial, acción, estado final (null = transición inválida).
        { EstadoDispositivo.Disponible, nameof(Dispositivo.Asignar), EstadoDispositivo.Asignado },
        { EstadoDispositivo.Disponible, nameof(Dispositivo.Liberar), null },
        { EstadoDispositivo.Disponible, nameof(Dispositivo.EnviarAMantenimiento), EstadoDispositivo.Mantenimiento },
        { EstadoDispositivo.Disponible, nameof(Dispositivo.DarDeBaja), EstadoDispositivo.DadoDeBaja },
        { EstadoDispositivo.Disponible, nameof(Dispositivo.Reactivar), null },

        { EstadoDispositivo.Asignado, nameof(Dispositivo.Asignar), null },
        { EstadoDispositivo.Asignado, nameof(Dispositivo.Liberar), EstadoDispositivo.Disponible },
        { EstadoDispositivo.Asignado, nameof(Dispositivo.EnviarAMantenimiento), null },
        { EstadoDispositivo.Asignado, nameof(Dispositivo.DarDeBaja), null },
        { EstadoDispositivo.Asignado, nameof(Dispositivo.Reactivar), null },

        { EstadoDispositivo.Mantenimiento, nameof(Dispositivo.Asignar), null },
        { EstadoDispositivo.Mantenimiento, nameof(Dispositivo.Liberar), null },
        { EstadoDispositivo.Mantenimiento, nameof(Dispositivo.EnviarAMantenimiento), null },
        { EstadoDispositivo.Mantenimiento, nameof(Dispositivo.DarDeBaja), EstadoDispositivo.DadoDeBaja },
        { EstadoDispositivo.Mantenimiento, nameof(Dispositivo.Reactivar), EstadoDispositivo.Disponible },

        { EstadoDispositivo.DadoDeBaja, nameof(Dispositivo.Asignar), null },
        { EstadoDispositivo.DadoDeBaja, nameof(Dispositivo.Liberar), null },
        { EstadoDispositivo.DadoDeBaja, nameof(Dispositivo.EnviarAMantenimiento), null },
        { EstadoDispositivo.DadoDeBaja, nameof(Dispositivo.DarDeBaja), null },
        { EstadoDispositivo.DadoDeBaja, nameof(Dispositivo.Reactivar), EstadoDispositivo.Disponible },
    };

    [Theory]
    [MemberData(nameof(Transiciones))]
    public void Cada_transicion_valida_cambia_el_estado_y_las_invalidas_se_rechazan(
        EstadoDispositivo inicial, string accion, EstadoDispositivo? esperado)
    {
        var dispositivo = EnEstado(inicial);

        if (esperado is null)
        {
            Assert.Throws<ExcepcionDeDominio>(() => Ejecutar(dispositivo, accion));
            Assert.Equal(inicial, dispositivo.Estado);
        }
        else
        {
            Ejecutar(dispositivo, accion);
            Assert.Equal(esperado, dispositivo.Estado);
        }
    }

    [Fact]
    public void Un_dispositivo_asignado_no_puede_ir_a_mantenimiento_ni_darse_de_baja()
    {
        var dispositivo = EnEstado(EstadoDispositivo.Asignado);

        Assert.False(dispositivo.PuedeIrAMantenimiento);
        Assert.False(dispositivo.PuedeDarseDeBaja);
    }

    private static Dispositivo EnEstado(EstadoDispositivo estado)
    {
        var dispositivo = Dispositivo.Registrar("ESP32-001", null);
        switch (estado)
        {
            case EstadoDispositivo.Asignado:
                dispositivo.Asignar();
                break;
            case EstadoDispositivo.Mantenimiento:
                dispositivo.EnviarAMantenimiento();
                break;
            case EstadoDispositivo.DadoDeBaja:
                dispositivo.DarDeBaja();
                break;
        }

        Assert.Equal(estado, dispositivo.Estado);
        return dispositivo;
    }

    private static void Ejecutar(Dispositivo dispositivo, string accion)
    {
        switch (accion)
        {
            case nameof(Dispositivo.Asignar): dispositivo.Asignar(); break;
            case nameof(Dispositivo.Liberar): dispositivo.Liberar(); break;
            case nameof(Dispositivo.EnviarAMantenimiento): dispositivo.EnviarAMantenimiento(); break;
            case nameof(Dispositivo.DarDeBaja): dispositivo.DarDeBaja(); break;
            case nameof(Dispositivo.Reactivar): dispositivo.Reactivar(); break;
            default: throw new ArgumentOutOfRangeException(nameof(accion));
        }
    }
}
