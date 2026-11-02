using Vitalify.Domain.Dispositivos;

namespace Vitalify.Domain.Vinculacion;

/// <summary>
/// Contenido del QR que dibuja el wearable: <c>VTF1:{codigoDispositivo}:{codigo}</c>, por ejemplo
/// <c>VTF1:ESP32-001:K7P3QX9M</c>. Todo va en mayúsculas para que el QR use el modo alfanumérico (con un código de
/// dispositivo de hasta 32 caracteres son 46 caracteres: entra en la versión 3 con corrección M, 29 × 29 módulos, que
/// cabe en la OLED de 128 × 64). No lleva ningún dato del paciente.
/// </summary>
public static class FormatoQr
{
    public const string Prefijo = "VTF1";

    public static string Componer(string codigoDispositivo, string codigo) =>
        $"{Prefijo}:{Dispositivo.NormalizarCodigo(codigoDispositivo)}:{CodigoVinculacion.Normalizar(codigo)}";

    /// <summary>Separa el código del dispositivo y el de vinculación. False si el texto no tiene el formato.</summary>
    public static bool TryInterpretar(string? texto, out string codigoDispositivo, out string codigo)
    {
        codigoDispositivo = string.Empty;
        codigo = string.Empty;

        var partes = (texto ?? string.Empty).Trim().Split(':');
        if (partes.Length != 3
            || !string.Equals(partes[0], Prefijo, StringComparison.OrdinalIgnoreCase)
            || !Dispositivo.CodigoValido(partes[1])
            || !CodigoVinculacion.FormatoValido(partes[2]))
        {
            return false;
        }

        codigoDispositivo = Dispositivo.NormalizarCodigo(partes[1]);
        codigo = CodigoVinculacion.Normalizar(partes[2]);
        return true;
    }
}
