using Vitalify.Domain.Comun;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Domain.Tests.Telemetria;

public class EstadoYVigenciaTests
{
    private static readonly DateTime T0 = new(2026, 9, 30, 14, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Noventa = TimeSpan.FromSeconds(90);
    private static readonly Guid Hospitalizacion = Guid.NewGuid();

    private static LecturaSignos Lectura(DateTime medidoEn, SignosValidos signos, bool caida = false) =>
        LecturaSignos.Registrar("ESP32-001", medidoEn, medidoEn, Hospitalizacion, Guid.NewGuid(), signos, caida, 1, OrigenLectura.Simulador);

    private static SignosValidos Signos(int? fc = null, decimal? temp = null, int? bateria = null) =>
        new(fc, null, null, temp, null, null, bateria);

    [Fact]
    public void Aplicar_actualiza_cada_variable_con_su_propia_hora()
    {
        var estado = EstadoSignosActual.Iniciar(Hospitalizacion, Guid.NewGuid(), "ESP32-001");

        estado.Aplicar(Lectura(T0, Signos(fc: 80, temp: 36.8m, bateria: 90)));
        estado.Aplicar(Lectura(T0.AddSeconds(10), Signos(fc: 84)));

        Assert.Equal((84, T0.AddSeconds(10)), (estado.FcValor, estado.FcMedidoEn));
        Assert.Equal((36.8m, T0), (estado.TempValor, estado.TempMedidoEn));
        Assert.Equal(90, estado.Bateria);
        Assert.Equal(T0.AddSeconds(10), estado.UltimaLecturaEn);
    }

    [Fact]
    public void Una_lectura_atrasada_no_pisa_valores_mas_nuevos_pero_completa_los_que_faltan()
    {
        var estado = EstadoSignosActual.Iniciar(Hospitalizacion, Guid.NewGuid(), "ESP32-001");
        estado.Aplicar(Lectura(T0, Signos(fc: 80, bateria: 70)));

        estado.Aplicar(Lectura(T0.AddMinutes(-5), Signos(fc: 120, temp: 37.0m, bateria: 95)));

        Assert.Equal((80, T0), (estado.FcValor, estado.FcMedidoEn));
        Assert.Equal((37.0m, T0.AddMinutes(-5)), (estado.TempValor, estado.TempMedidoEn));
        Assert.Equal(T0, estado.UltimaLecturaEn);
        Assert.Equal(70, estado.Bateria);
    }

    [Fact]
    public void Una_caida_registra_su_hora()
    {
        var estado = EstadoSignosActual.Iniciar(Hospitalizacion, Guid.NewGuid(), "ESP32-001");

        estado.Aplicar(Lectura(T0, Signos(), caida: true));

        Assert.Equal(T0, estado.UltimaCaidaEn);
        Assert.Null(estado.FcValor);
    }

    [Fact]
    public void Una_lectura_sin_signos_ni_caida_no_se_puede_registrar()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Lectura(T0, Signos(bateria: 50)));
    }

    [Theory]
    [InlineData(0, EstadoVariable.Vigente)]
    [InlineData(89, EstadoVariable.Vigente)]
    [InlineData(90, EstadoVariable.Vigente)]
    [InlineData(91, EstadoVariable.PendienteActualizacion)]
    public void Una_variable_queda_pendiente_cuando_supera_la_vigencia(int segundos, EstadoVariable esperado)
    {
        Assert.Equal(esperado, Vigencia.EstadoDe(T0, T0.AddSeconds(segundos), Noventa));
    }

    [Fact]
    public void Sin_medicion_la_variable_no_tiene_datos()
    {
        Assert.Equal(EstadoVariable.SinDatos, Vigencia.EstadoDe(null, T0, Noventa));
    }

    [Theory]
    [InlineData(null, EstadoSenal.SinDatos)]
    [InlineData(180, EstadoSenal.ConDatos)]
    [InlineData(181, EstadoSenal.SinSenal)]
    public void La_senal_se_pierde_tras_el_doble_de_la_vigencia(int? segundosDesdeUltima, EstadoSenal esperado)
    {
        DateTime? ultima = segundosDesdeUltima is null ? null : T0;
        Assert.Equal(esperado, Vigencia.SenalDe(ultima, T0.AddSeconds(segundosDesdeUltima ?? 0), Noventa));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    [InlineData(-24 * 3600, true)]
    [InlineData(-24 * 3600 - 1, false)]
    public void La_ventana_acepta_hasta_2_minutos_en_el_futuro_y_24_horas_de_retraso(int segundos, bool aceptada)
    {
        Assert.Equal(aceptada, VentanaDeRecepcion.PorDefecto.Validar(T0.AddSeconds(segundos), T0) is null);
    }
}
