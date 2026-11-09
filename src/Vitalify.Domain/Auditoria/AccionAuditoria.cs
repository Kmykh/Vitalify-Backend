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

    /// <summary>Consulta del historial, el resumen, las alertas o las notas de un paciente (fase 7).</summary>
    ConsultaHistorialPaciente = 23,
    NotaRegistrada = 24,
    NotaEditada = 25,

    // Fase 8: proceso de enfermería NNN. El detalle lleva solo ids: nunca el código ni la etiqueta del diagnóstico.
    ConfirmarDiagnosticoEnfermeria = 26,
    DescartarSugerenciaNnn = 27,
    CerrarDiagnosticoEnfermeria = 28,

    /// <summary>Ejecución de una intervención NIC del plan.</summary>
    RegistrarIntervencion = 29,
    ValorarResultado = 30,

    /// <summary>Intervención NIC agregada a un diagnóstico ya confirmado.</summary>
    AgregarIntervencionPlan = 31,

    /// <summary>El administrador marcó una entrada del catálogo NNN como verificada (dato del catálogo, no de un paciente).</summary>
    VerificarCatalogoNnn = 32,
}
