using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Vitalify.Application.Camas;
using Vitalify.Application.Dispositivos;
using Vitalify.Application.Pacientes;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Telemetria;
using Vitalify.Application.Usuarios;

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
        services.AddScoped<ListarIncidencias>();

        return services;
    }
}
