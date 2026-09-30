using Vitalify.Domain.Comun;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Domain.Tests.Usuarios;

public class CorreoTests
{
    [Theory]
    [InlineData("  Ana.Perez@Hospital.PE ", "ana.perez@hospital.pe")]
    [InlineData("MEDICO@VITALIFY.COM", "medico@vitalify.com")]
    [InlineData("enfermera@clinica.org", "enfermera@clinica.org")]
    public void Se_guarda_normalizado_sin_espacios_y_en_minusculas(string entrada, string esperado)
    {
        Assert.Equal(esperado, Correo.Crear(entrada).Valor);
    }

    [Fact]
    public void Dos_correos_que_solo_difieren_en_mayusculas_son_iguales()
    {
        Assert.Equal(Correo.Crear("Ana@Hospital.pe"), Correo.Crear("ana@hospital.pe "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-arroba.com")]
    [InlineData("ana@")]
    [InlineData("@hospital.pe")]
    [InlineData("ana@hospital")]
    [InlineData("ana perez@hospital.pe")]
    public void Rechaza_formatos_invalidos(string? entrada)
    {
        Assert.False(Correo.EsValido(entrada));
        Assert.Throws<ExcepcionDeDominio>(() => Correo.Crear(entrada));
    }

    [Fact]
    public void Rechaza_correos_de_mas_de_254_caracteres()
    {
        var largo = new string('a', 250) + "@x.pe";
        Assert.False(Correo.EsValido(largo));
    }
}
