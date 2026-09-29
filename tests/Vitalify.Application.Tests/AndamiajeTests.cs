namespace Vitalify.Application.Tests;

/// <summary>Prueba mínima para dejar armado el proyecto. Se reemplaza con pruebas de casos de uso desde la fase 1.</summary>
public class AndamiajeTests
{
    [Fact]
    public void El_ensamblado_de_aplicacion_se_carga()
    {
        Assert.Equal("Vitalify.Application", typeof(AssemblyReference).Assembly.GetName().Name);
    }
}
