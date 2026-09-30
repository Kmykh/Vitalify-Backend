using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Vitalify.Application.Sesiones;
using Vitalify.Application.Usuarios;

namespace Vitalify.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra los casos de uso y sus validadores. <see cref="OpcionesSesion"/> y los puertos los registra
    /// la infraestructura.
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

        return services;
    }
}
