using Vitalify.Application.Puertos;

namespace Vitalify.Api.IntegrationTests.Infraestructura;

/// <summary>Hora real más un desfase que las pruebas pueden adelantar y luego restablecer.</summary>
public sealed class RelojAjustable : IReloj
{
    private TimeSpan _desfase;

    public DateTime AhoraUtc => DateTime.UtcNow + _desfase;

    /// <summary>Adelanta el reloj; al desechar el resultado vuelve a la hora real.</summary>
    public IDisposable Adelantar(TimeSpan tiempo)
    {
        _desfase += tiempo;
        return new Restablecer(this);
    }

    private sealed class Restablecer(RelojAjustable reloj) : IDisposable
    {
        public void Dispose() => reloj._desfase = TimeSpan.Zero;
    }
}
