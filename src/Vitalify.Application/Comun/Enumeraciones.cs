namespace Vitalify.Application.Comun;

internal static class Enumeraciones
{
    /// <summary>Convierte el nombre de un valor del enum sin distinguir mayúsculas. No acepta números.</summary>
    public static bool TryParseNombre<TEnum>(string? valor, out TEnum resultado)
        where TEnum : struct, Enum
    {
        resultado = default;
        var texto = valor?.Trim();
        if (string.IsNullOrEmpty(texto) || !char.IsLetter(texto[0]))
        {
            return false;
        }

        return Enum.TryParse(texto, ignoreCase: true, out resultado) && Enum.IsDefined(resultado);
    }

    public static string Nombres<TEnum>() where TEnum : struct, Enum => string.Join(", ", Enum.GetNames<TEnum>());
}
