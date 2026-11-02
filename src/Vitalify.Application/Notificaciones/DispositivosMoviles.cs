using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Notificaciones;

namespace Vitalify.Application.Notificaciones;

public sealed record RegistrarDispositivoMovilComando(Guid UsuarioId, string? Token, string? Plataforma);

/// <summary>La app registra su token de FCM para recibir push. Si el token ya existía, pasa al usuario actual.</summary>
public sealed class RegistrarDispositivoMovil(IRepositorioDispositivosMoviles moviles, IUnidadDeTrabajo unidadDeTrabajo, IReloj reloj)
{
    public async Task<Resultado<Unidad>> EjecutarAsync(RegistrarDispositivoMovilComando comando, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(comando.Token) || comando.Token.Trim().Length > DispositivoMovil.LargoMaximoToken)
        {
            errores["token"] = ["El token de notificaciones es obligatorio."];
        }

        if (!Enumeraciones.TryParseNombre<PlataformaMovil>(comando.Plataforma, out var plataforma))
        {
            errores["plataforma"] = [$"La plataforma debe ser una de: {Enumeraciones.Nombres<PlataformaMovil>()}."];
        }

        if (errores.Count > 0)
        {
            return Error.Validacion(errores);
        }

        var ahora = reloj.AhoraUtc;
        if (await moviles.ObtenerPorHashAsync(DispositivoMovil.Hash(comando.Token!), ct) is { } existente)
        {
            existente.Renovar(comando.UsuarioId, plataforma, ahora);
        }
        else
        {
            moviles.Agregar(DispositivoMovil.Registrar(comando.UsuarioId, comando.Token!, plataforma, ahora));
        }

        return await unidadDeTrabajo.GuardarCambiosAsync(ct);
    }
}

/// <summary>La app quita su token (al cerrar sesión). Quitar un token que no existe no es un error.</summary>
public sealed class QuitarDispositivoMovil(IRepositorioDispositivosMoviles moviles, IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<Resultado<Unidad>> EjecutarAsync(Guid usuarioId, string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ErroresComunes.ValidacionDeCampo("token", "El token de notificaciones es obligatorio.");
        }

        if (await moviles.ObtenerPorHashAsync(DispositivoMovil.Hash(token), ct) is { } existente && existente.UsuarioId == usuarioId)
        {
            moviles.Eliminar(existente);
            return await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }

        return Unidad.Valor;
    }
}
