using FluentValidation;

namespace Vitalify.Application.Comun;

internal static class Validacion
{
    /// <summary>Valida y convierte los fallos en un <see cref="Error"/> de validación con los campos en camelCase.</summary>
    public static async Task<Error?> ValidarAsync<T>(this IValidator<T> validador, T instancia, CancellationToken ct)
    {
        var resultado = await validador.ValidateAsync(instancia, ct);
        if (resultado.IsValid)
        {
            return null;
        }

        var detalles = resultado.Errors
            .GroupBy(e => CamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        return Error.Validacion(detalles);
    }

    private static string CamelCase(string nombre) =>
        string.IsNullOrEmpty(nombre) ? nombre : char.ToLowerInvariant(nombre[0]) + nombre[1..];
}
