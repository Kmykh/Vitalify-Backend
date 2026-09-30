namespace Vitalify.Infrastructure.Seguridad;

/// <summary>Nombres de los claims del access token (se validan sin mapear a los tipos de .NET).</summary>
public static class ClaimsVitalify
{
    public const string Sub = "sub";
    public const string Email = "email";
    public const string Nombre = "name";
    public const string Rol = "role";
    public const string Jti = "jti";
    public const string Expiracion = "exp";
}
