using Vitalify.Domain.Comun;
using Vitalify.Domain.Sesiones;

namespace Vitalify.Domain.Tests.Sesiones;

public class TokenRevocadoTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Requiere_jti(string jti)
    {
        Assert.Throws<ExcepcionDeDominio>(() => TokenRevocado.Crear(jti, Guid.NewGuid(), DateTime.UtcNow));
    }

    [Fact]
    public void Rechaza_jti_demasiado_largo()
    {
        var jti = new string('a', TokenRevocado.LargoMaximoJti + 1);
        Assert.Throws<ExcepcionDeDominio>(() => TokenRevocado.Crear(jti, Guid.NewGuid(), DateTime.UtcNow));
    }
}
