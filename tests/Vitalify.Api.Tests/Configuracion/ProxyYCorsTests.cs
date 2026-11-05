using System.Net;
using Microsoft.Extensions.Configuration;
using Vitalify.Api.Configuracion;

namespace Vitalify.Api.Tests.Configuracion;

public sealed class ProxyYCorsTests
{
    private static IConfiguration Configuracion(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Clave, v => (string?)v.Valor)).Build();

    [Fact]
    public void Las_redes_del_proxy_se_leen_de_un_texto_con_comas_o_de_una_lista()
    {
        var texto = ProxyVitalify.LeerRedes(Configuracion(("Proxy:RedesConocidas", "172.30.0.0/24, 10.0.0.5")));
        var lista = ProxyVitalify.LeerRedes(Configuracion(("Proxy:RedesConocidas:0", "fd00::/8")));

        Assert.Equal([(IPAddress.Parse("172.30.0.0"), 24), (IPAddress.Parse("10.0.0.5"), 32)], texto.Select(r => (r.Prefix, r.PrefixLength)));
        Assert.Equal((IPAddress.Parse("fd00::"), 8), (lista.Single().Prefix, lista.Single().PrefixLength));
        Assert.Empty(ProxyVitalify.LeerRedes(Configuracion())); // sin configurar, no se confía en ningún proxy
    }

    [Theory]
    [InlineData("no-es-una-ip/24")]
    [InlineData("172.30.0.0/33")]
    public void Una_red_invalida_no_deja_arrancar(string red) =>
        Assert.Throws<InvalidOperationException>(() => ProxyVitalify.LeerRedes(Configuracion(("Proxy:RedesConocidas", red))));

    [Fact]
    public void Los_origenes_se_leen_de_un_texto_con_comas_sin_barra_final()
    {
        var origenes = CorsVitalify.LeerOrigenes(Configuracion(("Cors:Origenes", "https://dashboard.example.com/, https://otro.example.com")));

        Assert.Equal(["https://dashboard.example.com", "https://otro.example.com"], origenes);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.example.com")]
    public void Cors_nunca_admite_comodines(string origen) =>
        Assert.Throws<InvalidOperationException>(() => CorsVitalify.LeerOrigenes(Configuracion(("Cors:Origenes", origen))));
}
