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
    QrGenerado = 14,
    QrEscaneado = 15,
    QrRechazado = 16,
    VinculacionPorQr = 17,
    EscalaSpo2Cambiada = 18,
    ProgramacionCargada = 19,
    AlertaTomada = 20,
    AlertaAtendida = 21,
    ServicioAsignado = 22,
}
