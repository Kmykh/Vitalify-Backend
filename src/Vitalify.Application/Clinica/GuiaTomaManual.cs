using System.Text.Json;

namespace Vitalify.Application.Clinica;

/// <summary>
/// Guía versionada del asistente de toma manual: preguntas, opciones, recomendaciones y fuentes. Es un recurso
/// embebido de este ensamblado (<c>Clinica/Guias/toma-manual.json</c>), no un dato de la base: cambiarla es publicar
/// una versión nueva del backend, y cada observación guarda la <see cref="Version"/> con la que se tomó.
/// </summary>
public sealed class GuiaTomaManual
{
    public const string Recurso = "Vitalify.Application.Clinica.Guias.toma-manual.json";

    private static readonly Lazy<GuiaTomaManual> Instancia = new(Cargar);

    private GuiaTomaManual(string json, string version)
    {
        Json = json;
        Version = version;
    }

    public static GuiaTomaManual Actual => Instancia.Value;

    /// <summary>El JSON tal como lo dibuja la app.</summary>
    public string Json { get; }

    /// <summary>Por ejemplo <c>2026.11.1</c>.</summary>
    public string Version { get; }

    private static GuiaTomaManual Cargar()
    {
        using var flujo = typeof(GuiaTomaManual).Assembly.GetManifestResourceStream(Recurso)
            ?? throw new InvalidOperationException($"No se encontró el recurso embebido {Recurso}.");
        using var lector = new StreamReader(flujo);
        var json = lector.ReadToEnd();

        using var documento = JsonDocument.Parse(json);
        var version = documento.RootElement.GetProperty("version").GetString();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("La guía de toma manual no tiene versión.");
        }

        return new GuiaTomaManual(json, version);
    }
}

/// <summary>Devuelve la guía de toma manual vigente.</summary>
public sealed class ObtenerGuiaTomaManual
{
    public GuiaTomaManual Ejecutar() => GuiaTomaManual.Actual;
}
