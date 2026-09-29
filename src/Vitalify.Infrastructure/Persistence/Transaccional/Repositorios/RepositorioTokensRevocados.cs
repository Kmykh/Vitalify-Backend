using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Sesiones;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

/// <summary>
/// Se consulta en cada request autenticado, así que tiene un <see cref="IMemoryCache"/> delante:
/// un token revocado se recuerda hasta que expira, y uno no revocado durante <see cref="DuracionNoRevocado"/>.
/// Con varias instancias de la API (fase 7), un logout tarda como máximo ese tiempo en verse en las demás.
/// </summary>
internal sealed class RepositorioTokensRevocados(TransaccionalDbContext db, IMemoryCache cache, IReloj reloj) : IRepositorioTokensRevocados
{
    public static readonly TimeSpan DuracionNoRevocado = TimeSpan.FromSeconds(30);

    public async Task<bool> EstaRevocadoAsync(string jti, CancellationToken ct = default)
    {
        if (cache.TryGetValue(Clave(jti), out bool revocado))
        {
            return revocado;
        }

        var expiraEn = await db.TokensRevocados.AsNoTracking()
            .Where(t => t.Jti == jti)
            .Select(t => (DateTime?)t.ExpiraEn)
            .SingleOrDefaultAsync(ct);

        if (expiraEn is { } expira)
        {
            RecordarRevocado(jti, expira);
            return true;
        }

        cache.Set(Clave(jti), false, DuracionNoRevocado);
        return false;
    }

    public void Agregar(TokenRevocado token)
    {
        db.TokensRevocados.Add(token);

        // Se marca en caché antes de guardar: si el guardado fallara, esta instancia igual rechaza el token
        // (falla hacia el lado seguro).
        RecordarRevocado(token.Jti, token.ExpiraEn);
    }

    private void RecordarRevocado(string jti, DateTime expiraEn)
    {
        var restante = expiraEn - reloj.AhoraUtc;
        if (restante > TimeSpan.Zero)
        {
            cache.Set(Clave(jti), true, restante);
        }
    }

    private static string Clave(string jti) => $"token-revocado:{jti}";
}
