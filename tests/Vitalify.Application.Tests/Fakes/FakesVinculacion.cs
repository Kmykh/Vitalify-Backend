using Vitalify.Application.Puertos;
using Vitalify.Application.Vinculacion;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Application.Tests.Fakes;

public sealed class RepositorioCodigosEnMemoria : IRepositorioCodigosVinculacion
{
    public List<CodigoVinculacion> Codigos { get; } = [];

    public Task<CodigoVinculacion?> ObtenerActivoAsync(Guid dispositivoId, CancellationToken ct = default) =>
        Task.FromResult(Codigos.SingleOrDefault(c => c.DispositivoId == dispositivoId && c.EstaActivo));

    public Task<CodigoVinculacion?> ObtenerPorHashTokenAsync(string hashToken, CancellationToken ct = default) =>
        Task.FromResult(Codigos.SingleOrDefault(c => c.TokenHash == hashToken));

    public void Agregar(CodigoVinculacion codigo) => Codigos.Add(codigo);
}

/// <summary>Registra los comandos publicados al wearable, en orden.</summary>
public sealed class PublicadorComandosFalso : IPublicadorComandosDispositivo
{
    public List<(string Dispositivo, ComandoDispositivo Comando, bool Retenido)> Publicados { get; } = [];

    public IEnumerable<T> De<T>(string dispositivo) where T : ComandoDispositivo =>
        Publicados.Where(p => p.Dispositivo == dispositivo).Select(p => p.Comando).OfType<T>();

    public Task PublicarAsync(string codigoDispositivo, ComandoDispositivo comando, bool retenido, CancellationToken ct = default)
    {
        Publicados.Add((codigoDispositivo, comando, retenido));
        return Task.CompletedTask;
    }
}
