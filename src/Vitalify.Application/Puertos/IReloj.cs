namespace Vitalify.Application.Puertos;

/// <summary>Hora actual en UTC. Se usa en lugar de <c>DateTime.UtcNow</c> para poder probar vencimientos.</summary>
public interface IReloj
{
    DateTime AhoraUtc { get; }
}
