using System.Collections.Concurrent;
using Vitalify.Application.Puertos;

namespace Vitalify.Infrastructure.Telemetria;

/// <summary>
/// Estado online/offline de cada wearable, en memoria (singleton). No hace falta persistirlo: el broker retiene el
/// último mensaje de <c>device/{codigo}/estado</c> y lo vuelve a entregar cuando la API se reconecta.
/// </summary>
internal sealed class PresenciaDispositivosEnMemoria : IPresenciaDispositivos
{
    private readonly ConcurrentDictionary<string, PresenciaDispositivo> _presencias = new(StringComparer.OrdinalIgnoreCase);

    public void Registrar(string codigoDispositivo, PresenciaDispositivo presencia) => _presencias[codigoDispositivo] = presencia;

    public PresenciaDispositivo? Obtener(string codigoDispositivo) => _presencias.GetValueOrDefault(codigoDispositivo);
}
