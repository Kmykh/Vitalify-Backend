namespace Vitalify.Api.Contratos;

/// <summary>Credenciales para iniciar sesión.</summary>
/// <param name="Correo">Correo registrado.</param>
/// <param name="Contrasena">Contraseña.</param>
public sealed record LoginSolicitud(string Correo, string Contrasena);

/// <summary>Refresh token recibido en el login o en el último refresh.</summary>
public sealed record RefrescarSolicitud(string RefreshToken);

/// <summary>Opcionalmente, el refresh token de la sesión a cerrar.</summary>
public sealed record CerrarSesionSolicitud(string? RefreshToken);

/// <summary>Datos de la cuenta a registrar.</summary>
/// <param name="Nombre">Nombre completo.</param>
/// <param name="Correo">Correo, único en el sistema.</param>
/// <param name="Contrasena">Al menos 8 caracteres, con al menos una letra y un número.</param>
/// <param name="Rol"><c>Medico</c> o <c>Enfermera</c>.</param>
public sealed record RegistrarUsuarioSolicitud(string Nombre, string Correo, string Contrasena, string Rol);
