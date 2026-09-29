using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;

namespace Vitalify.Application.Usuarios;

public sealed record ListarUsuariosConsulta(int Pagina = 1, int Tamano = 20);

public sealed class ListarUsuariosValidador : AbstractValidator<ListarUsuariosConsulta>
{
    public const int TamanoMaximo = 100;

    public ListarUsuariosValidador()
    {
        RuleFor(c => c.Pagina).GreaterThanOrEqualTo(1).WithMessage("La página debe ser 1 o mayor.");
        RuleFor(c => c.Tamano).InclusiveBetween(1, TamanoMaximo)
            .WithMessage($"El tamaño de página debe estar entre 1 y {TamanoMaximo}.");
    }
}

/// <summary>Lista paginada de usuarios. Solo para administradores (lo exige la política de la API).</summary>
public sealed class ListarUsuarios(IValidator<ListarUsuariosConsulta> validador, IRepositorioUsuarios usuarios)
{
    public async Task<Resultado<Pagina<UsuarioDetalleDto>>> EjecutarAsync(ListarUsuariosConsulta consulta, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(consulta, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        var pagina = await usuarios.ListarAsync(consulta.Pagina, consulta.Tamano, ct);
        return pagina.Mapear(UsuarioDetalleDto.Desde);
    }
}
