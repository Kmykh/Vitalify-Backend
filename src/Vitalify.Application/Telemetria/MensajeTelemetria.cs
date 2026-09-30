using System.Globalization;
using System.Text.Json;
using Vitalify.Application.Comun;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Telemetria;

/// <summary>
/// Interpreta el JSON que publica el firmware (<c>docs/contrato-telemetria.md</c>). Lo usan todos los adaptadores
/// de entrada (endpoint de desarrollo y, en la fase 7, MQTT), para que el contrato se interprete en un solo lugar.
/// </summary>
public static class MensajeTelemetria
{
    public const int LargoMaximoDispositivo = 64;

    private static readonly (string Campo, Func<SignosMedidos, decimal?, SignosMedidos> Asignar)[] Variables =
    [
        ("fc", (s, v) => s with { Fc = v }),
        ("fr", (s, v) => s with { Fr = v }),
        ("spo2", (s, v) => s with { Spo2 = v }),
        ("temp", (s, v) => s with { Temperatura = v }),
        ("pas", (s, v) => s with { Pas = v }),
        ("pad", (s, v) => s with { Pad = v }),
        ("bateria", (s, v) => s with { Bateria = v }),
    ];

    public static Resultado<LecturaTelemetria> Interpretar(ReadOnlySpan<byte> utf8, OrigenLectura origen)
    {
        try
        {
            using var documento = JsonDocument.Parse(utf8.ToArray());
            return Interpretar(documento.RootElement, origen);
        }
        catch (JsonException)
        {
            return ErroresComunes.ValidacionDeCampo("mensaje", "El mensaje no es un JSON válido.");
        }
    }

    public static Resultado<LecturaTelemetria> Interpretar(JsonElement json, OrigenLectura origen)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            return ErroresComunes.ValidacionDeCampo("mensaje", "El mensaje debe ser un objeto JSON.");
        }

        var errores = new Dictionary<string, string[]>();

        var dispositivo = json.TryGetProperty("dispositivo", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()!.Trim() : null;
        if (string.IsNullOrEmpty(dispositivo) || dispositivo.Length > LargoMaximoDispositivo)
        {
            errores["dispositivo"] = [$"dispositivo es obligatorio (texto de hasta {LargoMaximoDispositivo} caracteres)."];
        }

        DateTime? medidoEn = json.TryGetProperty("ts", out var ts) ? LeerMarcaDeTiempo(ts) : null;
        if (medidoEn is null)
        {
            errores["ts"] = ["ts es obligatorio: fecha ISO 8601 en UTC o epoch en milisegundos."];
        }

        long seq = 0;
        if (!json.TryGetProperty("seq", out var s) || s.ValueKind != JsonValueKind.Number || !s.TryGetInt64(out seq) || seq < 0)
        {
            errores["seq"] = ["seq es obligatorio: entero mayor o igual a 0."];
        }

        var signos = new SignosMedidos();
        foreach (var (campo, asignar) in Variables)
        {
            if (!json.TryGetProperty(campo, out var valor) || valor.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out var numero))
            {
                signos = asignar(signos, numero);
            }
            else
            {
                errores[campo] = [$"{campo} debe ser un número."];
            }
        }

        var caida = false;
        if (json.TryGetProperty("caida", out var c) && c.ValueKind != JsonValueKind.Null)
        {
            if (c.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                caida = c.GetBoolean();
            }
            else
            {
                errores["caida"] = ["caida debe ser true o false."];
            }
        }

        if (errores.Count > 0)
        {
            return Error.Validacion(errores);
        }

        return new LecturaTelemetria(dispositivo!.ToUpperInvariant(), medidoEn!.Value, seq, signos, caida, origen);
    }

    /// <summary>ISO 8601 (sin zona se asume UTC) o epoch en milisegundos. Se trunca a milisegundos.</summary>
    private static DateTime? LeerMarcaDeTiempo(JsonElement ts)
    {
        DateTimeOffset? momento = ts.ValueKind switch
        {
            JsonValueKind.String when DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var iso) => iso,
            JsonValueKind.Number when ts.TryGetInt64(out var ms) && ms is >= 0 and <= 253_402_300_799_999 =>
                DateTimeOffset.FromUnixTimeMilliseconds(ms),
            _ => null,
        };

        if (momento is not { } m)
        {
            return null;
        }

        var utc = m.UtcDateTime;
        return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
    }
}
