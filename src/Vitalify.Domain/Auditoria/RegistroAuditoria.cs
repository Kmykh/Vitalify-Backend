using Vitalify.Domain.Comun;

namespace Vitalify.Domain.Auditoria;

/// <summary>
/// Evento de seguridad. <see cref="UsuarioId"/> es quien realizó la acción (vacío si no se pudo identificar).
/// Nunca debe contener contraseñas ni tokens.
/// </summary>
public sealed class RegistroAuditoria
{
    public const int LargoMaximoIp = 45;
    public const int LargoMaximoDetalle = 1000;

    public Guid Id { get; private set; }
    public Guid? UsuarioId { get; private set; }
    public AccionAuditoria Accion { get; private set; }
    public string? Ip { get; private set; }
    public DateTime Fecha { get; private set; }
    public string? Detalle { get; private set; }

    private RegistroAuditoria()
    {
    }

    public static RegistroAuditoria Registrar(
        AccionAuditoria accion, DateTime fecha, Guid? usuarioId = null, string? ip = null, string? detalle = null)
    {
        if (!Enum.IsDefined(accion))
        {
            throw new ExcepcionDeDominio("La acción de auditoría no es válida.");
        }

        return new RegistroAuditoria
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Accion = accion,
            Ip = Recortar(ip, LargoMaximoIp),
            Fecha = fecha,
            Detalle = Recortar(detalle, LargoMaximoDetalle),
        };
    }

    private static string? Recortar(string? valor, int largo) =>
        valor is { Length: > 0 } && valor.Length > largo ? valor[..largo] : valor;
}
