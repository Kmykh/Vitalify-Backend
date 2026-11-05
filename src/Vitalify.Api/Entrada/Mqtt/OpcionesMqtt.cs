using System.Globalization;

namespace Vitalify.Api.Entrada.Mqtt;

/// <summary>
/// Sección <c>Mqtt</c> (variables <c>Mqtt__*</c> del .env). Debe coincidir con el broker: en desarrollo, el del proyecto
/// IoT (<c>IOT/tesis_V01/broker</c>); en producción, el Mosquitto de <c>docker-compose.prod.yml</c>, al que la API llega
/// por la red interna de Docker (<c>mosquitto:1883</c>, sin TLS). Con <see cref="UsarTls"/> sirve con cualquier broker
/// con TLS (por ejemplo, uno en la nube).
/// </summary>
public sealed record OpcionesMqtt
{
    public const string Seccion = "Mqtt";

    /// <summary>Tópicos del firmware (<c>config.h</c>): <c>+</c> es el código de cada dispositivo.</summary>
    public const string TopicoTelemetria = "device/+/telemetria";

    public const string TopicoEstado = "device/+/estado";

    public bool Habilitado { get; init; }
    public string Servidor { get; init; } = "localhost";
    public int Puerto { get; init; } = 1883;
    public string? Usuario { get; init; }
    public string? Clave { get; init; }

    /// <summary>Fijo: con la sesión persistente, el broker guarda lo que llegue mientras el backend está caído.</summary>
    public string ClientId { get; init; } = "vitalify-backend";

    public bool SesionPersistente { get; init; } = true;

    /// <summary>Conectar con TLS (<c>Mqtt__UsarTls</c>, false por defecto). El nombre del servidor se usa para SNI y para validar el certificado.</summary>
    public bool UsarTls { get; init; }

    /// <summary>
    /// Validar el certificado del broker contra las CA del sistema (<c>Mqtt__ValidarCertificado</c>, true por defecto).
    /// Solo se puede desactivar en Development (por ejemplo, con un certificado autofirmado).
    /// </summary>
    public bool ValidarCertificado { get; init; } = true;

    /// <param name="configuration">Configuración de la API (sección <c>Mqtt</c>).</param>
    /// <param name="esDesarrollo">Solo en Development se acepta <c>Mqtt__ValidarCertificado=false</c>.</param>
    public static OpcionesMqtt Cargar(IConfiguration configuration, bool esDesarrollo)
    {
        var s = configuration.GetSection(Seccion);
        var opciones = new OpcionesMqtt
        {
            Habilitado = bool.TryParse(s["Habilitado"], out var habilitado) && habilitado,
            Servidor = string.IsNullOrWhiteSpace(s["Servidor"]) ? "localhost" : s["Servidor"]!.Trim(),
            Puerto = int.TryParse(s["Puerto"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var puerto) ? puerto : 1883,
            Usuario = string.IsNullOrWhiteSpace(s["Usuario"]) ? null : s["Usuario"]!.Trim(),
            Clave = string.IsNullOrEmpty(s["Clave"]) ? null : s["Clave"],
            ClientId = string.IsNullOrWhiteSpace(s["ClientId"]) ? "vitalify-backend" : s["ClientId"]!.Trim(),
            SesionPersistente = !bool.TryParse(s["SesionPersistente"], out var persistente) || persistente,
            UsarTls = bool.TryParse(s["UsarTls"], out var tls) && tls,
            ValidarCertificado = !bool.TryParse(s["ValidarCertificado"], out var validar) || validar,
        };

        if (!opciones.ValidarCertificado && !esDesarrollo)
        {
            throw new InvalidOperationException(
                "Mqtt__ValidarCertificado=false solo se permite en Development: en producción el certificado del broker siempre se valida.");
        }

        if (opciones.Habilitado && opciones.Usuario is not null && opciones.Clave is null)
        {
            throw new InvalidOperationException(
                "Falta Mqtt__Clave en el .env: es MQTT_BACKEND_CLAVE del archivo broker/.env del proyecto IoT.");
        }

        return opciones;
    }
}
