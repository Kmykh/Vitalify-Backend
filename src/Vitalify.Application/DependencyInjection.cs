using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Vitalify.Application.Alertas;
using Vitalify.Application.Camas;
using Vitalify.Application.Clinica;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Notificaciones;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Servicios;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Telemetria;
using Vitalify.Application.Turnos;
using Vitalify.Application.Usuarios;
using Vitalify.Application.Vinculacion;

namespace Vitalify.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra los casos de uso y sus validadores. <see cref="OpcionesSesion"/> y los puertos los registra
    /// la infraestructura. Los validadores son singleton: no guardan estado (los que usan <c>IReloj</c> lo
    /// reciben también como singleton).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<RegistrarUsuarioComando>, RegistrarUsuarioValidador>();
        services.AddSingleton<IValidator<ListarUsuariosConsulta>, ListarUsuariosValidador>();
        services.AddSingleton<IValidator<IniciarSesionComando>, IniciarSesionValidador>();
        services.AddSingleton<IValidator<RefrescarSesionComando>, RefrescarSesionValidador>();

        services.AddScoped<RegistrarUsuario>();
        services.AddScoped<ObtenerUsuarioActual>();
        services.AddScoped<ObtenerUsuarioPorId>();
        services.AddScoped<ListarUsuarios>();
        services.AddScoped<IniciarSesion>();
        services.AddScoped<RefrescarSesion>();
        services.AddScoped<CerrarSesion>();

        services.AddSingleton<IValidator<RegistrarCamaComando>, RegistrarCamaValidador>();
        services.AddSingleton<IValidator<RegistrarDispositivoComando>, RegistrarDispositivoValidador>();
        services.AddSingleton<IValidator<CambiarEstadoDispositivoComando>, CambiarEstadoDispositivoValidador>();
        services.AddSingleton<IValidator<RegistrarIngresoPacienteComando>, RegistrarIngresoPacienteValidador>();
        services.AddSingleton<IValidator<ActualizarDatosPacienteComando>, ActualizarDatosPacienteValidador>();
        services.AddSingleton<IValidator<RegistrarEgresoComando>, RegistrarEgresoValidador>();
        services.AddSingleton<IValidator<ListarPacientesHospitalizadosConsulta>, ListarPacientesHospitalizadosValidador>();

        services.AddScoped<RegistrarCama>();
        services.AddScoped<ListarCamas>();
        services.AddScoped<RegistrarDispositivo>();
        services.AddScoped<CambiarEstadoDispositivo>();
        services.AddScoped<ListarDispositivos>();
        services.AddScoped<RegistrarIngresoPaciente>();
        services.AddScoped<ActualizarDatosPaciente>();
        services.AddScoped<VincularDispositivo>();
        services.AddScoped<LiberarDispositivo>();
        services.AddScoped<RegistrarEgreso>();
        services.AddScoped<ListarPacientesHospitalizados>();
        services.AddScoped<ListarPacientesMonitoreados>();
        services.AddScoped<ObtenerPaciente>();
        services.AddScoped<ObtenerHospitalizacionActivaPorDispositivo>();

        services.AddSingleton<IValidator<ListarIncidenciasConsulta>, ListarIncidenciasValidador>();
        services.AddScoped<IRegistrarLectura, RegistrarLectura>();
        services.AddScoped<IListarDispositivosVinculados, ListarDispositivosVinculados>();
        services.AddScoped<ObtenerSignosActuales>();
        services.AddScoped<ObtenerHistorialSignos>();
        services.AddScoped<ListarIncidencias>();

        // Motor clínico (fase 4): se engancha a la ingesta como manejador de LecturaRegistrada.
        services.AddScoped<EvaluadorRiesgo>();
        services.AddScoped<IManejadorLecturaRegistrada, EvaluarRiesgoAlRegistrarLectura>();
        services.AddScoped<ObtenerRiesgoPaciente>();

        // Asistente de toma manual y escala de SpO2 por paciente (fase 5).
        services.AddSingleton<IValidator<TomaManualComando>, TomaManualValidador>();
        services.AddSingleton<TomaInicialValidador>();
        services.AddSingleton<IValidator<CambiarEscalaSpo2Comando>, CambiarEscalaSpo2Validador>();
        services.AddSingleton<ObtenerGuiaTomaManual>();
        services.AddScoped<PresentadorRiesgo>();
        services.AddScoped<ServicioObservaciones>();
        services.AddScoped<RegistrarObservacion>();
        services.AddScoped<CambiarEscalaSpo2>();

        // Vinculación del wearable por QR y comandos al dispositivo (fase 5).
        services.AddScoped<ServicioCodigosVinculacion>();
        services.AddScoped<SincronizarPantallaDispositivo>();
        services.AddScoped<RegistrarPresenciaDispositivo>();
        services.AddScoped<AtenderSolicitudDispositivo>();
        services.AddScoped<RegenerarQrDispositivo>();
        services.AddScoped<EscanearQr>();
        services.AddScoped<ConfirmarVinculacion>();
        services.AddScoped<RenovarCodigosVinculacion>();

        // Servicios, turnos de enfermería y entrega de turno (fase 6).
        services.AddSingleton<IValidator<RegistrarServicioComando>, RegistrarServicioValidador>();
        services.AddScoped<RegistrarServicio>();
        services.AddScoped<ListarServicios>();
        services.AddScoped<AsignarServicio>();
        services.AddScoped<ObtenerProgramacion>();
        services.AddScoped<CargarProgramacion>();
        services.AddScoped<ObtenerTurnoActual>();
        services.AddScoped<ListarMisTurnos>();
        services.AddScoped<ObtenerEntregaTurno>();

        // Alertas clínicas y notificaciones (fase 6). Los manejadores se enganchan a LecturaRegistrada y RiesgoEvaluado.
        services.AddSingleton<IValidator<ListarAlertasConsulta>, ListarAlertasValidador>();
        services.AddSingleton<IValidator<AtenderAlertaComando>, AtenderAlertaValidador>();
        services.AddSingleton<LimitadorSignosTiempoReal>();
        services.AddScoped<ContextoHospitalizaciones>();
        services.AddScoped<AccesoServicio>();
        services.AddScoped<DestinatariosAlertas>();
        services.AddScoped<NotificadorAlertas>();
        services.AddScoped<ServicioAlertas>();
        services.AddScoped<PublicadorRiesgoEvaluado>();
        services.AddScoped<IManejadorRiesgoEvaluado, AlertaDeRiesgoAlEvaluar>();
        services.AddScoped<IManejadorRiesgoEvaluado, PublicarRiesgoEnTiempoReal>();
        services.AddScoped<IManejadorLecturaRegistrada, AlertasAlRegistrarLectura>();
        services.AddScoped<IManejadorLecturaRegistrada, PublicarSignosEnTiempoReal>();
        services.AddScoped<VigilarAlertas>();
        services.AddScoped<TomarAlerta>();
        services.AddScoped<AtenderAlerta>();
        services.AddScoped<ListarAlertas>();
        services.AddScoped<ListarAlertasPaciente>();
        services.AddScoped<RegistrarDispositivoMovil>();
        services.AddScoped<QuitarDispositivoMovil>();

        return services;
    }
}
