using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vitalify.Application.Puertos;
using Vitalify.Application.Sesiones;
using Vitalify.Infrastructure.Persistence;
using Vitalify.Infrastructure.Persistence.Historial;
using Vitalify.Infrastructure.Persistence.Historial.Repositorios;
using Vitalify.Infrastructure.Persistence.Transaccional;
using Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;
using Vitalify.Infrastructure.Seguridad;
using Vitalify.Infrastructure.Semillas;
using Vitalify.Infrastructure.Notificaciones;
using Vitalify.Infrastructure.Telemetria;
using Vitalify.Infrastructure.Turnos;
using Vitalify.Infrastructure.Vinculacion;

namespace Vitalify.Infrastructure;

public static class DependencyInjection
{
    private static readonly TimeSpan TimeoutHealthCheck = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Registra los adaptadores de salida: los dos DbContext y sus health checks (<c>transaccional</c> e
    /// <c>historial</c>), los repositorios, la seguridad (hasher, tokens, reloj), la telemetría y las semillas
    /// (administrador y datos de demostración).
    /// Falla al arrancar si falta una cadena de conexión, si las dos suman más de 10 conexiones contra la misma base o si la
    /// configuración JWT es inválida.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var transaccional = CadenaDeConexion.Obtener(configuration, CadenaDeConexion.Transaccional);
        var historial = CadenaDeConexion.Obtener(configuration, CadenaDeConexion.Historial);
        CadenaDeConexion.ValidarPresupuesto(transaccional, historial);

        services.AddDbContext<TransaccionalDbContext>(o =>
            OpcionesNpgsql.Configurar(o, transaccional, TransaccionalDbContext.Esquema));
        services.AddDbContext<HistorialDbContext>(o =>
            OpcionesNpgsql.Configurar(o, historial, HistorialDbContext.Esquema));

        services.AddHealthChecks()
            .AddNpgSql(transaccional, name: "transaccional", tags: ["db"], timeout: TimeoutHealthCheck)
            .AddNpgSql(historial, name: "historial", tags: ["db"], timeout: TimeoutHealthCheck);

        var jwt = OpcionesJwt.Cargar(configuration);
        services.AddSingleton(jwt);
        services.AddSingleton(new OpcionesSesion(
            TimeSpan.FromMinutes(jwt.MinutosInactividad), TimeSpan.FromHours(jwt.HorasMaximasSesion)));

        services.AddMemoryCache();
        services.AddSingleton<IReloj, RelojSistema>();
        services.AddSingleton<IHasherContrasenas, HasherContrasenas>();
        services.AddSingleton<IGeneradorTokens, GeneradorTokens>();

        services.AddScoped<IRepositorioUsuarios, RepositorioUsuarios>();
        services.AddScoped<IRepositorioSesiones, RepositorioSesiones>();
        services.AddScoped<IRepositorioTokensRevocados, RepositorioTokensRevocados>();
        services.AddScoped<IRepositorioAuditoria, RepositorioAuditoria>();
        services.AddScoped<IRepositorioCamas, RepositorioCamas>();
        services.AddScoped<IRepositorioDispositivos, RepositorioDispositivos>();
        services.AddScoped<IRepositorioPacientes, RepositorioPacientes>();
        services.AddScoped<IRepositorioHospitalizaciones, RepositorioHospitalizaciones>();
        services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();

        // Telemetría: historial en HistorialDbContext (vitalify_historial), resolución de dispositivos con caché.
        services.AddSingleton(ConfiguracionTelemetria.CargarOpciones(configuration));
        services.AddSingleton(ConfiguracionTelemetria.CargarRangos(configuration));
        services.AddScoped<IRepositorioLecturas, RepositorioLecturas>();
        services.AddScoped<IRepositorioEstadoSignos, RepositorioEstadoSignos>();
        services.AddScoped<IRepositorioIncidencias, RepositorioIncidencias>();
        services.AddScoped<IUnidadDeTrabajoHistorial, UnidadDeTrabajoHistorial>();
        services.AddScoped<IResolutorDispositivos, ResolutorDispositivosConCache>();
        services.AddSingleton<ISolicitudNuevaLectura, SolicitudNuevaLecturaPorComando>();
        services.AddSingleton<IPresenciaDispositivos, PresenciaDispositivosEnMemoria>();

        // Motor clínico: observaciones y evaluaciones en el historial.
        services.AddSingleton(ConfiguracionTelemetria.CargarOpcionesClinicas(configuration));
        services.AddScoped<IRepositorioObservaciones, RepositorioObservaciones>();
        services.AddScoped<IRepositorioEvaluaciones, RepositorioEvaluaciones>();

        // Vinculación por QR: códigos en el esquema transaccional; los comandos al wearable van por MQTT. Este es el
        // adaptador por defecto (solo log): la API lo reemplaza por el de MQTT cuando Mqtt__Habilitado=true.
        services.AddSingleton(ConfiguracionTelemetria.CargarOpcionesVinculacion(configuration));
        services.AddScoped<IRepositorioCodigosVinculacion, RepositorioCodigosVinculacion>();
        services.AddSingleton<IPublicadorComandosDispositivo, PublicadorComandosEnLog>();

        // Fase 6: servicios, turnos, alertas y notificaciones. El push por FCM solo con Push__Habilitado=true; el de
        // tiempo real (SignalR) lo registra la API.
        services.AddSingleton(ConfiguracionTelemetria.CargarCatalogoTurnos(configuration));
        services.AddSingleton(ConfiguracionTelemetria.CargarOpcionesAlertas(configuration));
        var push = ConfiguracionTelemetria.CargarOpcionesPush(configuration);
        services.AddSingleton(push);
        services.AddScoped<IRepositorioServicios, RepositorioServicios>();
        services.AddScoped<IRepositorioProgramacion, RepositorioProgramacion>();
        services.AddScoped<IRepositorioAlertas, RepositorioAlertas>();
        services.AddScoped<IRepositorioNotas, RepositorioNotas>();
        services.AddScoped<IRepositorioDispositivosMoviles, RepositorioDispositivosMoviles>();
        services.AddScoped<ITurnoVigente, TurnoVigenteEnBase>();
        services.AddSingleton<IBitacoraFallos, BitacoraFallosEnLog>();
        if (push.Habilitado)
        {
            services.AddSingleton<INotificadorPush, NotificadorPushFcm>();
        }
        else
        {
            services.AddSingleton<INotificadorPush, NotificadorPushEnLog>();
        }

        services.AddHostedService<SemillaAdministrador>();
        services.AddHostedService<SemillaDatosDemo>();

        return services;
    }
}
