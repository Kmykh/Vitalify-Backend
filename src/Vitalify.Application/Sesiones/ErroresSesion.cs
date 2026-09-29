using Vitalify.Application.Comun;

namespace Vitalify.Application.Sesiones;

public static class ErroresSesion
{
    /// <summary>Mismo error para correo inexistente, contraseña incorrecta o usuario inactivo.</summary>
    public static readonly Error CredencialesInvalidas =
        Error.NoAutorizado("credenciales-invalidas", "Correo o contraseña incorrectos.");

    /// <summary>La sesión venció por inactividad o por duración máxima: el cliente debe volver al login.</summary>
    public static readonly Error SesionExpirada =
        Error.NoAutorizado("sesion-expirada", "La sesión expiró. Vuelve a iniciar sesión.");

    public static readonly Error TokenInvalido =
        Error.NoAutorizado("token-invalido", "El refresh token no es válido o fue revocado.");
}
