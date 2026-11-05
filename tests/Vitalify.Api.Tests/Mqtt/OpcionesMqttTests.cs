using Microsoft.Extensions.Configuration;
using Vitalify.Api.Entrada.Mqtt;

namespace Vitalify.Api.Tests.Mqtt;

public sealed class OpcionesMqttTests
{
    private static IConfiguration Configuracion(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => $"Mqtt:{v.Clave}", v => (string?)v.Valor)).Build();

    [Fact]
    public void Por_defecto_sin_tls_y_validando_el_certificado()
    {
        var opciones = OpcionesMqtt.Cargar(Configuracion(), esDesarrollo: false);

        Assert.Equal((false, true, "localhost", 1883), (opciones.UsarTls, opciones.ValidarCertificado, opciones.Servidor, opciones.Puerto));
    }

    [Fact]
    public void Con_tls_hacia_un_broker_externo()
    {
        var opciones = OpcionesMqtt.Cargar(Configuracion(("UsarTls", "true"), ("Servidor", "broker.example.com"), ("Puerto", "8883")), esDesarrollo: false);

        Assert.Equal((true, true, "broker.example.com", 8883), (opciones.UsarTls, opciones.ValidarCertificado, opciones.Servidor, opciones.Puerto));
    }

    [Fact]
    public void Desactivar_la_validacion_del_certificado_solo_se_permite_en_Development()
    {
        var configuracion = Configuracion(("UsarTls", "true"), ("ValidarCertificado", "false"));

        Assert.False(OpcionesMqtt.Cargar(configuracion, esDesarrollo: true).ValidarCertificado);
        var error = Assert.Throws<InvalidOperationException>(() => OpcionesMqtt.Cargar(configuracion, esDesarrollo: false));
        Assert.Contains("Development", error.Message, StringComparison.Ordinal);
    }
}
