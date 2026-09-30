using System.Text.RegularExpressions;
using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Dispositivos;

/// <summary>
/// Wearable ESP32. <see cref="Codigo"/> es el <c>{id}</c> del tópico MQTT <c>device/{id}/telemetria</c>.
/// </summary>
/// <remarks>
/// Transiciones válidas:
/// <code>
/// Disponible    --Asignar-->             Asignado
/// Asignado      --Liberar-->             Disponible
/// Disponible    --EnviarAMantenimiento-> Mantenimiento
/// Disponible    --DarDeBaja-->           DadoDeBaja
/// Mantenimiento --DarDeBaja-->           DadoDeBaja
/// Mantenimiento --Reactivar-->           Disponible
/// DadoDeBaja    --Reactivar-->           Disponible
/// </code>
/// Un dispositivo asignado solo puede liberarse: no pasa a mantenimiento ni se da de baja.
/// </remarks>
public sealed partial class Dispositivo
{
    public const int LargoMaximoCodigo = 32;
    public const int LargoMaximoDescripcion = 200;

    public Guid Id { get; private set; }
    public string Codigo { get; private set; } = null!;
    public string? Descripcion { get; private set; }
    public EstadoDispositivo Estado { get; private set; }

    private Dispositivo()
    {
    }

    public bool PuedeAsignarse => Estado == EstadoDispositivo.Disponible;
    public bool PuedeLiberarse => Estado == EstadoDispositivo.Asignado;
    public bool PuedeIrAMantenimiento => Estado == EstadoDispositivo.Disponible;
    public bool PuedeDarseDeBaja => Estado is EstadoDispositivo.Disponible or EstadoDispositivo.Mantenimiento;
    public bool PuedeReactivarse => Estado is EstadoDispositivo.Mantenimiento or EstadoDispositivo.DadoDeBaja;

    public static Dispositivo Registrar(string codigo, string? descripcion)
    {
        var codigoNormalizado = NormalizarCodigo(codigo);
        if (!CodigoValido(codigoNormalizado))
        {
            throw new ExcepcionDeDominio("El código del dispositivo no es válido.");
        }

        var descripcionLimpia = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
        if (descripcionLimpia?.Length > LargoMaximoDescripcion)
        {
            throw new ExcepcionDeDominio($"La descripción no puede superar {LargoMaximoDescripcion} caracteres.");
        }

        return new Dispositivo
        {
            Id = Guid.NewGuid(),
            Codigo = codigoNormalizado,
            Descripcion = descripcionLimpia,
            Estado = EstadoDispositivo.Disponible,
        };
    }

    public static string NormalizarCodigo(string? codigo) => (codigo ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Patrón <c>^[A-Z0-9-]{3,32}$</c> (después de normalizar a mayúsculas).</summary>
    public static bool CodigoValido(string? codigo) => codigo is not null && Formato().IsMatch(NormalizarCodigo(codigo));

    public void Asignar() => CambiarA(EstadoDispositivo.Asignado, PuedeAsignarse);

    public void Liberar() => CambiarA(EstadoDispositivo.Disponible, PuedeLiberarse);

    public void EnviarAMantenimiento() => CambiarA(EstadoDispositivo.Mantenimiento, PuedeIrAMantenimiento);

    public void DarDeBaja() => CambiarA(EstadoDispositivo.DadoDeBaja, PuedeDarseDeBaja);

    public void Reactivar() => CambiarA(EstadoDispositivo.Disponible, PuedeReactivarse);

    private void CambiarA(EstadoDispositivo destino, bool permitido)
    {
        if (!permitido)
        {
            throw new ExcepcionDeDominio($"Un dispositivo en estado {Estado} no puede pasar a {destino}.");
        }

        Estado = destino;
    }

    [GeneratedRegex("^[A-Z0-9-]{3,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Formato();
}
