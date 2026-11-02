using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Camas;

namespace Vitalify.Application.Camas;

/// <summary>Cama con su ocupación. No incluye quién la ocupa: el administrador no ve datos de pacientes.</summary>
public sealed record CamaDto(Guid Id, string Codigo, string Servicio, Guid ServicioId, bool Activa, bool Ocupada);

public static class ErroresCama
{
    public static readonly Error CodigoDuplicado = Error.Conflicto("cama-codigo-duplicado", "Ya existe una cama con ese código.");

    public static readonly Error Ocupada =
        Error.Conflicto("cama-ocupada", "La cama ya está ocupada por otra hospitalización activa.");
}

public sealed record RegistrarCamaComando(string Codigo, Guid? ServicioId);

public sealed class RegistrarCamaValidador : AbstractValidator<RegistrarCamaComando>
{
    public RegistrarCamaValidador()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(c => c.Codigo)
            .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("El código de la cama es obligatorio.")
            .Must(Cama.CodigoValido).WithMessage("El código debe tener de 2 a 20 letras, números o guiones (por ejemplo MED-B-05).");

        RuleFor(c => c.ServicioId)
            .Must(s => s is not null && s != Guid.Empty).WithMessage("El servicio es obligatorio (ver GET /servicios).");
    }
}

public sealed class RegistrarCama(
    IValidator<RegistrarCamaComando> validador, IRepositorioCamas camas, IRepositorioServicios servicios, IUnidadDeTrabajo unidadDeTrabajo)
{
    public async Task<Resultado<CamaDto>> EjecutarAsync(RegistrarCamaComando comando, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(comando, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var servicio = await servicios.ObtenerPorIdAsync(comando.ServicioId!.Value, ct);
        if (servicio is not { Activo: true })
        {
            return ErroresComunes.ValidacionDeCampo("servicioId", "El servicio no existe o no está activo.");
        }

        var cama = Cama.Registrar(comando.Codigo, servicio.Id);
        if (await camas.ExisteCodigoAsync(cama.Codigo, ct))
        {
            return ErroresCama.CodigoDuplicado;
        }

        camas.Agregar(cama);
        var guardado = await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return guardado.EsExito ? new CamaDto(cama.Id, cama.Codigo, servicio.Nombre, servicio.Id, cama.Activa, Ocupada: false) : guardado.Error;
    }
}

public sealed class ListarCamas(IRepositorioCamas camas)
{
    public async Task<Resultado<IReadOnlyList<CamaDto>>> EjecutarAsync(bool soloDisponibles, CancellationToken ct = default) =>
        Resultado<IReadOnlyList<CamaDto>>.Exito(await camas.ListarAsync(soloDisponibles, ct));
}
