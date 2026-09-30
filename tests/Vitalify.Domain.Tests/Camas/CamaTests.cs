using Vitalify.Domain.Camas;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Tests.Camas;

public class CamaTests
{
    [Fact]
    public void Registrar_normaliza_el_codigo_y_queda_activa()
    {
        var cama = Cama.Registrar(" med-b-05 ", " Medicina B ");

        Assert.Equal("MED-B-05", cama.Codigo);
        Assert.Equal("Medicina B", cama.Servicio);
        Assert.True(cama.Activa);
    }

    [Theory]
    [InlineData("")]
    [InlineData("X")]
    [InlineData("MED B 05")]
    [InlineData("MED_B_05")]
    [InlineData("CODIGO-DEMASIADO-LARGO-01")]
    public void Rechaza_codigos_invalidos(string codigo)
    {
        Assert.Throws<ExcepcionDeDominio>(() => Cama.Registrar(codigo, "Medicina B"));
    }

    [Fact]
    public void El_servicio_es_obligatorio()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Cama.Registrar("MED-B-01", " "));
    }
}
