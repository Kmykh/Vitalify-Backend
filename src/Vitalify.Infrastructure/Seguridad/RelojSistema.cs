using Vitalify.Application.Puertos;

namespace Vitalify.Infrastructure.Seguridad;

internal sealed class RelojSistema : IReloj
{
    public DateTime AhoraUtc => DateTime.UtcNow;
}
