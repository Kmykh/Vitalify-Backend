namespace Vitalify.Domain.Auditoria;

public enum AccionAuditoria
{
    LoginExitoso = 1,
    LoginFallido = 2,
    Logout = 3,
    UsuarioCreado = 4,
    AccesoDenegado = 5,
    SesionExpirada = 6,
    PacienteIngresado = 7,
    PacienteActualizado = 8,
    PacienteEgresado = 9,
    DispositivoVinculado = 10,
    DispositivoLiberado = 11,
    ConsultaFichaPaciente = 12,
    ObservacionRegistrada = 13,
}
