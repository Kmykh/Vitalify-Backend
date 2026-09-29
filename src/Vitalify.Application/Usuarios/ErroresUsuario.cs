using Vitalify.Application.Comun;

namespace Vitalify.Application.Usuarios;

public static class ErroresUsuario
{
    public static readonly Error CorreoEnUso = Error.Conflicto("correo-en-uso", "El correo ya está en uso.");

    public static readonly Error NoEncontrado = Error.NoEncontrado("usuario-no-encontrado", "El usuario no existe.");
}
