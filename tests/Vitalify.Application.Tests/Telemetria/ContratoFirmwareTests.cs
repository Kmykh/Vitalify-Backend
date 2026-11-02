using System.Text;
using Vitalify.Application.Telemetria;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Tests.Telemetria;

/// <summary>
/// Prueba de contrato con el firmware del ESP32 (IOT/tesis_V01/tesis/pruebas/pruebas.cpp, pruebasTelemetria):
/// los mismos JSON que produce construirJsonTelemetria deben interpretarse sin errores.
/// </summary>
public class ContratoFirmwareTests
{
    private const string Completo =
        """{"dispositivo":"ESP32-001","ts":"2026-09-30T14:05:10.250Z","seq":18234,"fc":82,"spo2":97,"temp":36.8,"caida":false}""";

    private const string Parcial =
        """{"dispositivo":"ESP32-001","ts":"2026-09-30T14:05:10.250Z","seq":5,"temp":-0.5,"caida":true}""";

    [Fact]
    public void El_JSON_completo_del_firmware_se_interpreta_y_no_descarta_nada()
    {
        var lectura = MensajeTelemetria.Interpretar(Encoding.UTF8.GetBytes(Completo), OrigenLectura.Mqtt).Valor;

        Assert.Equal(("ESP32-001", 18234L, false), (lectura.CodigoDispositivo, lectura.Seq, lectura.Caida));
        Assert.Equal(new DateTime(2026, 9, 30, 14, 5, 10, 250, DateTimeKind.Utc), lectura.MedidoEn);
        Assert.Equal(new SignosMedidos(Fc: 82, Spo2: 97, Temperatura: 36.8m), lectura.Signos);
        Assert.Empty(EvaluadorSignos.Evaluar(lectura.Signos, RangosFisiologicos.PorDefecto).Descartes);
    }

    [Fact]
    public void El_JSON_parcial_del_firmware_conserva_la_caida_y_descarta_la_temperatura_imposible()
    {
        var lectura = MensajeTelemetria.Interpretar(Encoding.UTF8.GetBytes(Parcial), OrigenLectura.Mqtt).Valor;
        var evaluacion = EvaluadorSignos.Evaluar(lectura.Signos, RangosFisiologicos.PorDefecto);

        Assert.True(lectura.Caida);
        Assert.Equal((TipoIncidencia.FueraDeRango, VariableSigno.Temperatura), (evaluacion.Descartes.Single().Tipo, evaluacion.Descartes.Single().Variable));
        Assert.False(evaluacion.Validos.TieneAlgunSigno);
    }
}
