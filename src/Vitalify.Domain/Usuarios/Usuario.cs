using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Usuarios;

/// <summary>
/// Cuenta de acceso al sistema. El dominio solo conoce el hash de la contraseña, nunca el texto plano.
/// </summary>
public sealed class Usuario
{
    public const int LargoMaximoNombre = 150;

    public Guid Id { get; private set; }
    public string Nombre { get; private set; } = null!;
    public Correo Correo { get; private set; } = null!;
    public string HashContrasena { get; private set; } = null!;
    public Rol Rol { get; private set; }
    public bool Activo { get; private set; }
    public DateTime CreadoEn { get; private set; }
    public DateTime ActualizadoEn { get; private set; }

    private Usuario()
    {
    }

    public static Usuario Crear(string nombre, Correo correo, string hashContrasena, Rol rol, DateTime ahora)
    {
        var nombreLimpio = nombre?.Trim() ?? string.Empty;
        if (nombreLimpio.Length == 0)
        {
            throw new ExcepcionDeDominio("El nombre es obligatorio.");
        }

        if (nombreLimpio.Length > LargoMaximoNombre)
        {
            throw new ExcepcionDeDominio($"El nombre no puede superar {LargoMaximoNombre} caracteres.");
        }

        ArgumentNullException.ThrowIfNull(correo);

        if (string.IsNullOrWhiteSpace(hashContrasena))
        {
            throw new ExcepcionDeDominio("El hash de la contraseña es obligatorio.");
        }

        if (!Enum.IsDefined(rol))
        {
            throw new ExcepcionDeDominio("El rol no es válido.");
        }

        return new Usuario
        {
            Id = Guid.NewGuid(),
            Nombre = nombreLimpio,
            Correo = correo,
            HashContrasena = hashContrasena,
            Rol = rol,
            Activo = true,
            CreadoEn = ahora,
            ActualizadoEn = ahora,
        };
    }

    public void Desactivar(DateTime ahora)
    {
        Activo = false;
        ActualizadoEn = ahora;
    }
}
