using Microsoft.AspNetCore.Identity;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Seguridad;

/// <summary>
/// PBKDF2 mediante <see cref="PasswordHasher{TUser}"/> de Microsoft.Extensions.Identity.Core (sin el resto de
/// ASP.NET Identity). El hasher no usa la instancia del usuario, por eso se pasa <c>null</c>.
/// </summary>
internal sealed class HasherContrasenas : IHasherContrasenas
{
    private readonly PasswordHasher<Usuario> _hasher = new();
    private readonly Lazy<string> _hashFicticio;

    public HasherContrasenas() =>
        _hashFicticio = new Lazy<string>(() => Calcular(Convert.ToBase64String(Guid.NewGuid().ToByteArray())));

    public string Calcular(string contrasena) => _hasher.HashPassword(null!, contrasena);

    public bool Verificar(string? hash, string contrasena)
    {
        var resultado = _hasher.VerifyHashedPassword(null!, hash ?? _hashFicticio.Value, contrasena);
        return hash is not null && resultado != PasswordVerificationResult.Failed;
    }
}
