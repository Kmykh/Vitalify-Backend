using System.Globalization;

namespace Vitalify.Api.Entrada.Mqtt;

/// <summary>
/// Sección <c>Mqtt</c> (variables <c>Mqtt__*</c> del .env). Debe coincidir con el broker del proyecto IoT
/// (<c>IOT/tesis_V01/broker</c>): usuario <c>vitalify-backend</c> y su clave de <c>broker/.env</c>.
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

    public static OpcionesMqtt Cargar(IConfiguration configuration)
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
        };

        if (opciones.Habilitado && opciones.Usuario is not null && opciones.Clave is null)
        {
            throw new InvalidOperationException(
                "Falta Mqtt__Clave en el .env: es MQTT_BACKEND_CLAVE del archivo broker/.env del proyecto IoT.");
        }

        return opciones;
    }
}
