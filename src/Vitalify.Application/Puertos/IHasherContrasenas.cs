namespace Vitalify.Application.Puertos;

public interface IHasherContrasenas
{
    string Calcular(string contrasena);

    /// <summary>
    /// Verifica la contraseña contra el hash. Si <paramref name="hash"/> es nulo (el usuario no existe),
    /// igual hace una verificación completa contra un hash ficticio y devuelve <c>false</c>, para que el
    /// tiempo de respuesta no revele qué correos están registrados.
    /// </summary>
    bool Verificar(string? hash, string contrasena);
}
