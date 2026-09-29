namespace Vitalify.Domain.Tests;

/// <summary>Prueba mínima para dejar armado el proyecto. Se reemplaza con pruebas del dominio desde la fase 1.</summary>
public class AndamiajeTests
{
    [Fact]
    public void El_ensamblado_del_dominio_se_carga()
    {
        Assert.Equal("Vitalify.Domain", typeof(AssemblyReference).Assembly.GetName().Name);
    }
}
