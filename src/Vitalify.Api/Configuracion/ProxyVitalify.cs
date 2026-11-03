using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using RedIp = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Vitalify.Api.Configuracion;

/// <summary>
/// La API detrás de un proxy con HTTPS (Caddy en <c>docker-compose.prod.yml</c>). Solo se cree a
/// <c>X-Forwarded-For</c> y <c>X-Forwarded-Proto</c> si la conexión viene de una red de <c>Proxy__RedesConocidas</c>
/// (CIDR separados por coma, por ejemplo <c>172.30.0.0/24</c>): así el límite de logins se cuenta por la IP real del
/// cliente y nadie puede inventarse una IP mandando el encabezado directamente. Sin redes configuradas, no se confía en
/// ningún proxy (ni siquiera en localhost).
/// </summary>
internal static class ProxyVitalify
{
    public static IServiceCollection AddProxyVitalify(this IServiceCollection services, IConfiguration configuration, IHostEnvironment entorno)
    {
        var redes = LeerRedes(configuration);
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var red in redes)
            {
                o.KnownNetworks.Add(red);
            }

            // Un solo salto: la IP que agregó el proxy conocido (Caddy no deja pasar el X-Forwarded-For del cliente).
            o.ForwardLimit = 1;
        });

        if (!entorno.IsDevelopment())
        {
            services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(365));
            // Detrás del proxy, la API escucha solo HTTP (8080) y no sabe su puerto HTTPS: es el 443 del dominio.
            services.AddHttpsRedirection(o => o.HttpsPort = 443);
        }

        return services;
    }

    /// <summary><c>Proxy:RedesConocidas</c>: una lista (<c>Proxy__RedesConocidas__0</c>) o un texto con CIDR separados por coma.</summary>
    internal static IReadOnlyList<RedIp> LeerRedes(IConfiguration configuration)
    {
        var seccion = configuration.GetSection("Proxy:RedesConocidas");
        var textos = seccion.GetChildren().Select(h => h.Value).Append(seccion.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .SelectMany(v => v!.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return textos.Select(Interpretar).ToList();
    }

    private static RedIp Interpretar(string cidr)
    {
        var partes = cidr.Split('/', 2);
        if (!IPAddress.TryParse(partes[0], out var direccion))
        {
            throw new InvalidOperationException($"Proxy__RedesConocidas: '{cidr}' no es una red válida (ejemplo: 172.30.0.0/24).");
        }

        var maximo = direccion.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        var prefijo = maximo;
        if (partes.Length == 2 && (!int.TryParse(partes[1], out prefijo) || prefijo < 0 || prefijo > maximo))
        {
            throw new InvalidOperationException($"Proxy__RedesConocidas: '{cidr}' tiene un prefijo inválido.");
        }

        return new RedIp(direccion, prefijo);
    }
}
