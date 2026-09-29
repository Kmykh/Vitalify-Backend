namespace Vitalify.Application.Comun;

/// <summary>Política de contraseñas, compartida por el registro y la semilla del administrador.</summary>
public static class ReglasContrasena
{
    public const int LargoMinimo = 8;

    /// <summary>Límite superior para evitar entradas enormes en el cálculo del hash.</summary>
    public const int LargoMaximo = 128;

    public const string Mensaje = "La contraseña debe tener al menos 8 caracteres, con al menos una letra y un número.";

    public static bool EsValida(string? contrasena) =>
        contrasena is { Length: >= LargoMinimo and <= LargoMaximo }
        && contrasena.Any(char.IsLetter)
        && contrasena.Any(char.IsDigit);
}
