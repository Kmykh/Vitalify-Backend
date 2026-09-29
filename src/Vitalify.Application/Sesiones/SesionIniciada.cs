using Vitalify.Application.Usuarios;

namespace Vitalify.Application.Sesiones;

public sealed record SesionIniciada(string AccessToken, DateTime ExpiraEn, string RefreshToken, UsuarioDto Usuario);
