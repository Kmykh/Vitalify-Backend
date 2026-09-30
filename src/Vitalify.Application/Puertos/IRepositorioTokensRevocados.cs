using Vitalify.Domain.Sesiones;

namespace Vitalify.Application.Puertos;

public interface IRepositorioTokensRevocados
{
    Task<bool> EstaRevocadoAsync(string jti, CancellationToken ct = default);

    void Agregar(TokenRevocado token);
}
