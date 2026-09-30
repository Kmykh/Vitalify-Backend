using Microsoft.Extensions.Caching.Memory;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Puertos;
using Vitalify.Application.Telemetria;

namespace Vitalify.Infrastructure.Telemetria;

/// <summary>
/// Resuelve la hospitalización de un dispositivo con <c>ObtenerHospitalizacionActivaPorDispositivo</c> (fase 2) y la
/// recuerda en <see cref="IMemoryCache"/> durante <see cref="OpcionesTelemetria.CacheDispositivos"/> (15 s por
/// defecto), para no consultar la base en cada lectura. Vincular, liberar y egresar la invalidan en esta
/// instancia; en otra instancia, el cambio tarda como máximo esa duración en verse.
/// </summary>
internal sealed class ResolutorDispositivosConCache(
    ObtenerHospitalizacionActivaPorDispositivo obtenerHospitalizacion,
    IRepositorioDispositivos dispositivos,
    IMemoryCache cache,
    OpcionesTelemetria opciones) : IResolutorDispositivos
{
    public async Task<ResolucionDispositivo> ResolverAsync(string codigoDispositivo, CancellationToken ct = default)
    {
        var clave = Clave(codigoDispositivo);
        if (cache.TryGetValue(clave, out ResolucionDispositivo? enCache) && enCache is not null)
        {
            return enCache;
        }

        var hospitalizacion = await obtenerHospitalizacion.EjecutarAsync(codigoDispositivo, ct);
        var resolucion = hospitalizacion is not null
            ? new ResolucionDispositivo(EstadoDispositivoEnIngesta.Vinculado, hospitalizacion)
            : new ResolucionDispositivo(
                await dispositivos.ExisteCodigoAsync(codigoDispositivo, ct) ? EstadoDispositivoEnIngesta.SinVincular : EstadoDispositivoEnIngesta.Desconocido,
                null);

        if (opciones.CacheDispositivos > TimeSpan.Zero)
        {
            cache.Set(clave, resolucion, opciones.CacheDispositivos);
        }

        return resolucion;
    }

    public void Invalidar(string codigoDispositivo) => cache.Remove(Clave(codigoDispositivo));

    private static string Clave(string codigo) => $"resolucion-dispositivo:{codigo.Trim().ToUpperInvariant()}";
}
