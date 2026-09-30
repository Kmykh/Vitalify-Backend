namespace Vitalify.Domain.Auditoria;

public enum AccionAuditoria
{
    LoginExitoso = 1,
    LoginFallido = 2,
    Logout = 3,
    UsuarioCreado = 4,
    AccesoDenegado = 5,
    SesionExpirada = 6,
}
