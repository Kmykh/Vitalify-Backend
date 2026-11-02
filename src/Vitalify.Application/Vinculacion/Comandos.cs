using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Vitalify.Application.Vinculacion;

/// <summary>
/// Comando del backend al wearable (<c>docs/contrato-telemetria.md</c>, "Comandos al wearable"). Cambiar estos JSON es
/// cambiar el firmware. Ninguno lleva el nombre, el documento ni el diagnóstico del paciente: solo la cama y las
/// iniciales en <see cref="Vinculado"/>.
/// </summary>
public abstract record ComandoDispositivo
{
    public abstract string Accion { get; }

    /// <summary>Si el contrato lo publica retenido (el broker se lo entrega al wearable al reconectarse).</summary>
    public abstract bool Retenido { get; }
}

/// <summary>Dibujar el QR de vinculación. Retenido.</summary>
/// <param name="Qr">Contenido del QR: <c>VTF1:{codigoDispositivo}:{codigo}</c>.</param>
public sealed record MostrarQr(string Qr, DateTime Expira) : ComandoDispositivo
{
    public override string Accion => "mostrar-qr";
    public override bool Retenido => true;
}

/// <summary>Una enfermera escaneó el QR y está registrando al paciente. No retenido.</summary>
public sealed record Emparejando : ComandoDispositivo
{
    public override string Accion => "emparejando";
    public override bool Retenido => false;
}

/// <summary>Quedó vinculado: mostrar cama e iniciales y empezar a enviar telemetría. Retenido.</summary>
public sealed record Vinculado(string Cama, string Iniciales) : ComandoDispositivo
{
    public override string Accion => "vinculado";
    public override bool Retenido => true;
}

/// <summary>Se liberó (egreso o desvinculación): dejar de enviar telemetría. Retenido.</summary>
public sealed record Desvinculado : ComandoDispositivo
{
    public override string Accion => "desvinculado";
    public override bool Retenido => true;
}

/// <summary>Repetir una medición (por ejemplo, presión incompleta). No retenido.</summary>
public sealed record RepetirMedicion(string Variable, string Motivo) : ComandoDispositivo
{
    public override string Accion => "repetir-medicion";
    public override bool Retenido => false;
}

/// <summary>Tópicos y JSON de los comandos y de las solicitudes del wearable.</summary>
public static class ContratoComandos
{
    /// <summary>Suscripción del backend a lo que pide cada wearable (<c>{"accion":"solicitar-qr"}</c>).</summary>
    public const string TopicoSolicitudes = "device/+/solicitudes";

    public const string AccionSolicitarQr = "solicitar-qr";

    public static string Topico(string codigoDispositivo) => $"device/{codigoDispositivo}/comandos";

    /// <summary>
    /// JSON compacto con <c>accion</c> primero, por ejemplo
    /// <c>{"accion":"mostrar-qr","qr":"VTF1:ESP32-001:K7P3QX9M","expira":"2026-11-02T15:10:00Z"}</c>.
    /// </summary>
    public static byte[] Serializar(ComandoDispositivo comando)
    {
        ArgumentNullException.ThrowIfNull(comando);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("accion", comando.Accion);
            switch (comando)
            {
                case MostrarQr m:
                    json.WriteString("qr", m.Qr);
                    json.WriteString("expira", m.Expira.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
                    break;
                case Vinculado v:
                    json.WriteString("cama", v.Cama);
                    json.WriteString("iniciales", v.Iniciales);
                    break;
                case RepetirMedicion r:
                    json.WriteString("variable", r.Variable);
                    json.WriteString("motivo", r.Motivo);
                    break;
            }

            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static string SerializarTexto(ComandoDispositivo comando) => Encoding.UTF8.GetString(Serializar(comando));

    /// <summary>¿Es <c>{"accion":"solicitar-qr"}</c>? Cualquier otro contenido se ignora.</summary>
    public static bool EsSolicitudQr(ReadOnlySpan<byte> payload)
    {
        try
        {
            var lector = new Utf8JsonReader(payload);
            using var documento = JsonDocument.ParseValue(ref lector);
            return documento.RootElement.ValueKind == JsonValueKind.Object
                   && documento.RootElement.TryGetProperty("accion", out var accion)
                   && accion.ValueKind == JsonValueKind.String
                   && string.Equals(accion.GetString(), AccionSolicitarQr, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
