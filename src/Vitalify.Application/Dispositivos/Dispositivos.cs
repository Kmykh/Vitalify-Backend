using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Dispositivos;

namespace Vitalify.Application.Dispositivos;

/// <param name="CodigoCama">Cama a la que está vinculado, si está asignado (sin datos del paciente).</param>
public sealed record DispositivoDto(Guid Id, string Codigo, string? Descripcion, string Estado, string? CodigoCama)
{
    public static DispositivoDto Desde(Dispositivo dispositivo, string? codigoCama = null) =>
        new(dispositivo.Id, dispositivo.Codigo, dispositivo.Descripcion, dispositivo.Estado.ToString(), codigoCama);
}

public static class ErroresDispositivo
{
    public static readonly Error NoEncontrado = Error.NoEncontrado("dispositivo-no-encontrado", "El dispositivo no existe.");

    public static readonly Error CodigoDuplicado =
        Error.Conflicto("dispositivo-codigo-duplicado", "Ya existe un dispositivo con ese código.");

    public static readonly Error EnMantenimiento =
        Error.Conflicto("dispositivo-en-mantenimiento", "El sensor está en mantenimiento y no se puede vincular.");

    public static readonly Error DadoDeBaja =
        Error.Conflicto("dispositivo-dado-de-baja", "El sensor fue dado de baja y no se puede vincular.");

    /// <summary>Sin el detalle de la cama (por ejemplo, cuando lo detecta el índice único en una carrera).</summary>
    public static readonly Error YaVinculado =
        Error.Conflicto("dispositivo-ya-vinculado", "El sensor ya está vinculado a otra cama activa. Libéralo primero.");

    /// <summary>Indica la cama, pero nunca el nombre del paciente.</summary>
    public static Error YaVinculadoA(string codigoDispositivo, string codigoCama) =>
        Error.Conflicto("dispositivo-ya-vinculado",
            $"El sensor {codigoDispositivo} ya está vinculado a la cama {codigoCama}. Libéralo primero desde esa cama.");

    public static Error TransicionInvalida(Dispositivo dispositivo, EstadoDispositivo destino) =>
        Error.Conflicto("transicion-invalida", dispositivo.Estado == EstadoDispositivo.Asignado
            ? "El dispositivo está vinculado a un paciente: libéralo desde la ficha del paciente antes de cambiar su estado."
            : $"Un dispositivo en estado {dispositivo.Estado} no puede pasar a {destino}.");
}

public sealed record RegistrarDispositivoComando(string Codigo, string? Descripcion);

public sealed class RegistrarDispositivoValidador : AbstractValidator<RegistrarDispositivoComando>
{
    public RegistrarDispositivoValidador()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(c => c.Codigo)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("El código del dispositivo es obligatorio.")
            .Must(Dispositivo.CodigoValido)
            .WithMessage("El código debe tener de 3 a 32 letras mayúsculas, números o guiones (por ejemplo ESP32-001).");

        RuleFor(c => c.Descripcion)
            .Must(d => d is null || d.Trim().Length <= Dispositivo.LargoMaximoDescripcion)
            .WithMessage($"La descripción no puede superar {Dispositivo.LargoMaximoDescripcion} caracteres.");
    }
}

public sealed class RegistrarDispositivo(
    IValidator<RegistrarDispositivoComando> validador, IRepositorioDispositivos dispositivos, IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<Resultado<DispositivoDto>> EjecutarAsync(RegistrarDispositivoComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var dispositivo = Dispositivo.Registrar(comando.Codigo, comando.Descripcion);
        if (await dispositivos.ExisteCodigoAsync(dispositivo.Codigo, ct))
        {
            return ErroresDispositivo.CodigoDuplicado;
        }

        dispositivos.Agregar(dispositivo);
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? DispositivoDto.Desde(dispositivo) : guardado.Error;
    }
}

/// <param name="Estado"><c>Disponible</c> (reactivar), <c>Mantenimiento</c> o <c>DadoDeBaja</c>.</param>
public sealed record CambiarEstadoDispositivoComando(Guid DispositivoId, string Estado);

public sealed class CambiarEstadoDispositivoValidador : AbstractValidator<CambiarEstadoDispositivoComando>
{
    public static readonly IReadOnlyList<EstadoDispositivo> EstadosAdministrativos =
        [EstadoDispositivo.Disponible, EstadoDispositivo.Mantenimiento, EstadoDispositivo.DadoDeBaja];

    public CambiarEstadoDispositivoValidador()
    {
        RuleFor(c => c.Estado)
            .Must(e => Enumeraciones.TryParseNombre<EstadoDispositivo>(e, out var estado) && EstadosAdministrativos.Contains(estado))
            .WithMessage("El estado debe ser Disponible, Mantenimiento o DadoDeBaja. La asignación se hace vinculando el sensor a un paciente.");
    }
}

/// <summary>El administrador envía un dispositivo a mantenimiento, lo da de baja o lo reactiva.</summary>
public sealed class CambiarEstadoDispositivo(
    IValidator<CambiarEstadoDispositivoComando> validador, IRepositorioDispositivos dispositivos, IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<Resultado<DispositivoDto>> EjecutarAsync(CambiarEstadoDispositivoComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var dispositivo = await dispositivos.ObtenerPorIdAsync(comando.DispositivoId, ct);
        if (dispositivo is null)
        {
            return ErroresDispositivo.NoEncontrado;
        }

        Enumeraciones.TryParseNombre<EstadoDispositivo>(comando.Estado, out var destino);
        if (dispositivo.Estado == destino)
        {
            return DispositivoDto.Desde(dispositivo);
        }

        var permitido = destino switch
        {
            EstadoDispositivo.Disponible => dispositivo.PuedeReactivarse,
            EstadoDispositivo.Mantenimiento => dispositivo.PuedeIrAMantenimiento,
            _ => dispositivo.PuedeDarseDeBaja,
        };
        if (!permitido)
        {
            return ErroresDispositivo.TransicionInvalida(dispositivo, destino);
        }

        switch (destino)
        {
            case EstadoDispositivo.Disponible:
                dispositivo.Reactivar();
                break;
            case EstadoDispositivo.Mantenimiento:
                dispositivo.EnviarAMantenimiento();
                break;
            default:
                dispositivo.DarDeBaja();
                break;
        }

        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? DispositivoDto.Desde(dispositivo) : guardado.Error;
    }
}

public sealed class ListarDispositivos(IRepositorioDispositivos dispositivos)
{
    /// <param name="estado">Filtro opcional por nombre de estado.</param>
    public async Task<Resultado<IReadOnlyList<DispositivoDto>>> EjecutarAsync(string? estado, CancellationToken ct = default)
    {
        EstadoDispositivo? filtro = null;
        if (!string.IsNullOrWhiteSpace(estado))
        {
            if (!Enumeraciones.TryParseNombre<EstadoDispositivo>(estado, out var valor))
            {
                return ErroresComunes.ValidacionDeCampo("estado", $"El estado debe ser uno de: {Enumeraciones.Nombres<EstadoDispositivo>()}.");
            }

            filtro = valor;
        }

        return Resultado<IReadOnlyList<DispositivoDto>>.Exito(await dispositivos.ListarAsync(filtro, ct));
    }
}
