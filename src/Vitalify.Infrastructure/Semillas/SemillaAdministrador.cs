using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Semillas;

/// <summary>
/// Al arrancar, si no existe ningún administrador, crea uno con <c>Seed__AdminCorreo</c>, <c>Seed__AdminNombre</c>
/// y <c>Seed__AdminContrasena</c>. Si faltan, no crea nada y lo advierte en el log. Nunca usa valores por
/// defecto, y un fallo aquí no impide que la API arranque.
/// </summary>
internal sealed class SemillaAdministrador(
    IServiceScopeFactory scopes, IConfiguration configuration, ILogger<SemillaAdministrador> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await CrearSiHaceFaltaAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "No se pudo verificar o crear el administrador inicial.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CrearSiHaceFaltaAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var servicios = scope.ServiceProvider;
        var usuarios = servicios.GetRequiredService<IRepositorioUsuarios>();

        if (await usuarios.ExisteConRolAsync(Rol.Administrador, ct))
        {
            return;
        }

        var correoTexto = configuration["Seed:AdminCorreo"];
        var nombre = configuration["Seed:AdminNombre"];
        var contrasena = configuration["Seed:AdminContrasena"];

        if (string.IsNullOrWhiteSpace(correoTexto) || string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(contrasena))
        {
            logger.LogWarning(
                "No existe ningún administrador y faltan Seed__AdminCorreo, Seed__AdminNombre o Seed__AdminContrasena en el .env. " +
                "No se creó el administrador inicial.");
            return;
        }

        if (!Correo.TryCrear(correoTexto, out var correo))
        {
            logger.LogError("Seed__AdminCorreo no tiene un formato de correo válido. No se creó el administrador inicial.");
            return;
        }

        if (!ReglasContrasena.EsValida(contrasena))
        {
            logger.LogError("Seed__AdminContrasena no cumple la política: {Politica} No se creó el administrador inicial.", ReglasContrasena.Mensaje);
            return;
        }

        if (nombre.Trim().Length > Usuario.LargoMaximoNombre)
        {
            logger.LogError("Seed__AdminNombre supera {Largo} caracteres. No se creó el administrador inicial.", Usuario.LargoMaximoNombre);
            return;
        }

        if (await usuarios.ExisteCorreoAsync(correo, ct))
        {
            logger.LogError("Ya existe un usuario no administrador con el correo de Seed__AdminCorreo. No se creó el administrador inicial.");
            return;
        }

        var ahora = servicios.GetRequiredService<IReloj>().AhoraUtc;
        var hasher = servicios.GetRequiredService<IHasherContrasenas>();
        var administrador = Usuario.Crear(nombre, correo, hasher.Calcular(contrasena), Rol.Administrador, ahora);

        usuarios.Agregar(administrador);
        servicios.GetRequiredService<IRepositorioAuditoria>().Agregar(RegistroAuditoria.Registrar(
            AccionAuditoria.UsuarioCreado, ahora, detalle: $"Administrador inicial {administrador.Id} creado por la semilla."));

        var guardado = await servicios.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync(ct);
        if (guardado.EsExito)
        {
            logger.LogInformation("Administrador inicial creado (id {UsuarioId}).", administrador.Id);
        }
        else
        {
            logger.LogWarning("No se creó el administrador inicial: {Motivo}", guardado.Error.Mensaje);
        }
    }
}
