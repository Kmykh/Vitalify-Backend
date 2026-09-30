using System.Text;
using Vitalify.Application.Comun;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Tests.Telemetria;

public class MensajeTelemetriaTests
{
    private static Resultado<LecturaTelemetria> Interpretar(string json) =>
        MensajeTelemetria.Interpretar(Encoding.UTF8.GetBytes(json), OrigenLectura.Mqtt);

    [Fact]
    public void Interpreta_el_ejemplo_completo_del_contrato()
    {
        var resultado = Interpretar("""
            {"dispositivo":"esp32-001","ts":"2026-09-30T14:05:10Z","seq":18234,"fc":82,"fr":16,"spo2":97,
             "temp":36.8,"pas":118,"pad":76,"caida":false,"bateria":87}
            """);

        var lectura = resultado.Valor;
        Assert.Equal(("ESP32-001", 18234L, OrigenLectura.Mqtt, false), (lectura.CodigoDispositivo, lectura.Seq, lectura.Origen, lectura.Caida));
        Assert.Equal(new DateTime(2026, 9, 30, 14, 5, 10, DateTimeKind.Utc), lectura.MedidoEn);
        Assert.Equal(DateTimeKind.Utc, lectura.MedidoEn.Kind);
        Assert.Equal(new SignosMedidos(82, 16, 97, 36.8m, 118, 76, 87), lectura.Signos);
    }

    [Fact]
    public void Acepta_epoch_en_milisegundos_y_variables_ausentes()
    {
        var lectura = Interpretar("""{"dispositivo":"ESP32-001","ts":1790777110123,"seq":1,"fc":80}""").Valor;

        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790777110123).UtcDateTime, lectura.MedidoEn);
        Assert.Equal(new SignosMedidos(Fc: 80), lectura.Signos);
    }

    [Fact]
    public void Trunca_la_marca_de_tiempo_a_milisegundos_y_convierte_la_zona_a_UTC()
    {
        var lectura = Interpretar("""{"dispositivo":"ESP32-001","ts":"2026-09-30T09:05:10.1234567-05:00","seq":1}""").Valor;

        Assert.Equal(new DateTime(2026, 9, 30, 14, 5, 10, 123, DateTimeKind.Utc), lectura.MedidoEn);
    }

    [Fact]
    public void Los_campos_obligatorios_faltantes_se_informan_por_campo()
    {
        var resultado = Interpretar("""{"fc":"82","caida":"no"}""");

        Assert.Equal(TipoError.Validacion, resultado.Error.Tipo);
        Assert.Equal(["caida", "dispositivo", "fc", "seq", "ts"], resultado.Error.Detalles.Keys.Order());
    }

    [Theory]
    [InlineData("""{"dispositivo":"ESP32-001","ts":"ayer","seq":1}""", "ts")]
    [InlineData("""{"dispositivo":"ESP32-001","ts":"2026-09-30T14:05:10Z","seq":-1}""", "seq")]
    [InlineData("""{"dispositivo":"","ts":"2026-09-30T14:05:10Z","seq":1}""", "dispositivo")]
    [InlineData("""[1,2]""", "mensaje")]
    [InlineData("""{no es json""", "mensaje")]
    public void Rechaza_mensajes_malformados(string json, string campo)
    {
        Assert.Contains(campo, Interpretar(json).Error.Detalles.Keys);
    }
}
