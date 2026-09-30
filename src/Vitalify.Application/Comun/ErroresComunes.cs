namespace Vitalify.Application.Comun;

public static class ErroresComunes
{
    /// <summary>Otro usuario cambió el mismo registro a la vez (control de concurrencia optimista).</summary>
    public static readonly Error Concurrencia =
        Error.Conflicto("conflicto-concurrencia", "Otro usuario modificó este registro al mismo tiempo. Intenta de nuevo.");

    public static Error ValidacionDeCampo(string campo, string mensaje) =>
        Error.Validacion(new Dictionary<string, string[]> { [campo] = [mensaje] });
}
