using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Api.Seguridad;

/// <summary>
/// Políticas de autorización (HU03). Se aplican con <c>[Authorize(Policy = Politicas.X)]</c>.
/// La matriz de permisos está en <c>docs/matriz-permisos.md</c>.
/// </summary>
public static class Politicas
{
    public const string SoloAdministrador = nameof(SoloAdministrador);
    public const string PersonalClinico = nameof(PersonalClinico);
    public const string SoloMedico = nameof(SoloMedico);
    public const string SoloEnfermera = nameof(SoloEnfermera);

    /// <summary>Lectura de catálogos (camas y dispositivos) sin datos de pacientes.</summary>
    public const string AdministradorOPersonalClinico = nameof(AdministradorOPersonalClinico);

    /// <summary>
    /// Registra las políticas. Por defecto (<c>FallbackPolicy</c>) todo endpoint exige autenticación, salvo los
    /// marcados con <c>[AllowAnonymous]</c>.
    /// </summary>
    public static IServiceCollection AddAutorizacionVitalify(this IServiceCollection services)
    {
        services.AddAuthorization(o =>
        {
            o.AddPolicy(SoloAdministrador, p => p.RequireAuthenticatedUser().RequireRole(nameof(Rol.Administrador)));
            o.AddPolicy(PersonalClinico, p => p.RequireAuthenticatedUser().RequireRole(nameof(Rol.Medico), nameof(Rol.Enfermera)));
            o.AddPolicy(SoloMedico, p => p.RequireAuthenticatedUser().RequireRole(nameof(Rol.Medico)));
            o.AddPolicy(SoloEnfermera, p => p.RequireAuthenticatedUser().RequireRole(nameof(Rol.Enfermera)));
            o.AddPolicy(AdministradorOPersonalClinico, p => p.RequireAuthenticatedUser()
                .RequireRole(nameof(Rol.Administrador), nameof(Rol.Medico), nameof(Rol.Enfermera)));

            o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        });

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ManejadorResultadoAutorizacion>();
        return services;
    }
}
