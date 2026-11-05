using System.Text.RegularExpressions;
using FluentValidation;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Auditoria;

namespace Vitalify.Application.Auditoria;

/// <summary>
/// Un registro de la auditoría para el administrador: solo la acción, quién, cuándo, desde qué IP y los ids que menciona.
/// El texto de <c>detalle</c> no sale: puede llevar el motivo de un egreso (por ejemplo, un fallecimiento) o el correo de
/// un login fallido, y el administrador no ve datos clínicos (Ley 29733).
/// </summary>
/// <param name="Referencias">Ids de los recursos involucrados (paciente, hospitalización, alerta, dispositivo...), en orden.</param>
public sealed partial record AuditoriaDto(Guid Id, string Accion, Guid? UsuarioId, DateTime Fecha, string? Ip, IReadOnlyList<Guid> Referencias)
{
    public static AuditoriaDto Desde(RegistroAuditoria r) =>
        new(r.Id, r.Accion.ToString(), r.UsuarioId, r.Fecha, r.Ip, Ids(r.Detalle));

    private static List<Guid> Ids(string? detalle) =>
        detalle is null
            ? []
            : PatronGuid().Matches(detalle).Select(m => Guid.Parse(m.Value)).Distinct().ToList();

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PatronGuid();
}

/// <param name="Accion">Nombre de una <see cref="AccionAuditoria"/> (por ejemplo, <c>ConsultaHistorialPaciente</c>).</param>
public sealed record ListarAuditoriaConsulta(
    string? Accion = null, Guid? UsuarioId = null, DateTime? Desde = null, DateTime? Hasta = null, int Pagina = 1, int Tamano = 50);

public sealed class ListarAuditoriaValidador : AbstractValidator<ListarAuditoriaConsulta>
{
    public const int TamanoMaximo = 100;

    public ListarAuditoriaValidador()
    {
        RuleFor(c => c.Accion)
            .Must(a => a is null || Enumeraciones.TryParseNombre<AccionAuditoria>(a, out _))
            .WithMessage($"La acción debe ser una de: {Enumeraciones.Nombres<AccionAuditoria>()}.");
        RuleFor(c => c.Pagina).GreaterThanOrEqualTo(1).WithMessage("La página debe ser 1 o mayor.");
        RuleFor(c => c.Tamano).InclusiveBetween(1, TamanoMaximo).WithMessage($"El tamaño de página debe estar entre 1 y {TamanoMaximo}.");
        RuleFor(c => c.Hasta)
            .Must((c, hasta) => c.Desde is null || hasta is null || c.Desde <= hasta)
            .WithMessage("hasta debe ser posterior a desde.");
    }
}

/// <summary>Auditoría de seguridad para el administrador, la más reciente primero.</summary>
public sealed class ListarAuditoria(IValidator<ListarAuditoriaConsulta> validador, IRepositorioAuditoria auditoria)
{
    public async Task<Resultado<Pagina<AuditoriaDto>>> EjecutarAsync(ListarAuditoriaConsulta consulta, CancellationToken ct = default)
    {
        if (await validador.ValidarAsync(consulta, ct) is { } errorValidacion)
        {
            return errorValidacion;
        }

        AccionAuditoria? accion = Enumeraciones.TryParseNombre<AccionAuditoria>(consulta.Accion, out var a) ? a : null;
        var pagina = await auditoria.ListarAsync(
            new FiltroAuditoria(accion, consulta.UsuarioId, consulta.Desde?.ToUniversalTime(), consulta.Hasta?.ToUniversalTime()),
            consulta.Pagina, consulta.Tamano, ct);
        return pagina.Mapear(AuditoriaDto.Desde);
    }
}
