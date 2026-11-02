using Vitalify.Domain.Telemetria;

namespace Vitalify.Application.Telemetria;

/// <summary>
/// Lectura que entra al núcleo por <see cref="IRegistrarLectura"/>. Es el contrato del firmware ya interpretado
/// (ver <c>docs/contrato-telemetria.md</c>); <see cref="Origen"/> solo sirve para trazabilidad.
/// </summary>
public sealed record LecturaTelemetria(
    string CodigoDispositivo, DateTime MedidoEn, long Seq, SignosMedidos Signos, bool Caida, OrigenLectura Origen);

public enum EstadoIngesta
{
    /// <summary>Se guardó con todas sus variables.</summary>
    Aceptada = 1,

    /// <summary>Se guardó, pero se descartaron algunas variables (ver incidencias).</summary>
    AceptadaParcial = 2,

    /// <summary>Ya existía una lectura de ese dispositivo con esa marca de tiempo: se ignora sin error.</summary>
    Duplicada = 3,

    /// <summary>No se guardó (ver incidencias).</summary>
    Rechazada = 4,
}

public sealed record IncidenciaDto(
    Guid Id, DateTime OcurridaEn, string CodigoDispositivo, Guid? HospitalizacionId, string Tipo, string? Variable,
    decimal? ValorRecibido, string Detalle, bool RequiereNuevaLectura)
{
    public static IncidenciaDto Desde(IncidenciaTelemetria i) =>
        new(i.Id, i.OcurridaEn, i.CodigoDispositivo, i.HospitalizacionId, i.Tipo.ToString(), i.Variable?.ToString(),
            i.ValorRecibido, i.Detalle, i.RequiereNuevaLectura);
}

public sealed record ResultadoIngesta(string Estado, string CodigoDispositivo, DateTime MedidoEn, IReadOnlyList<IncidenciaDto> Incidencias)
{
    public static ResultadoIngesta De(EstadoIngesta estado, LecturaTelemetria lectura, IEnumerable<IncidenciaTelemetria> incidencias) =>
        new(estado.ToString(), lectura.CodigoDispositivo, lectura.MedidoEn, incidencias.Select(IncidenciaDto.Desde).ToList());
}

/// <summary>Parámetros de la ingesta (sección <c>Telemetria</c> de la configuración).</summary>
/// <param name="Vigencia">Tras este tiempo sin medición, una variable queda <c>pendiente-actualizacion</c>.</param>
/// <param name="Ventana">Marcas de tiempo aceptadas.</param>
/// <param name="CacheDispositivos">Cuánto se recuerda a qué hospitalización corresponde un dispositivo.</param>
public sealed record OpcionesTelemetria(TimeSpan Vigencia, VentanaDeRecepcion Ventana, TimeSpan CacheDispositivos)
{
    public static readonly OpcionesTelemetria PorDefecto =
        new(TimeSpan.FromSeconds(90), VentanaDeRecepcion.PorDefecto, TimeSpan.FromSeconds(15));
}

/// <summary>
/// Evento de aplicación que se publica después de guardar una lectura. Hoy nadie lo escucha: es el punto de
/// extensión de la fase 4 (NEWS2/MEWS) y la fase 5 (alertas).
/// </summary>
public sealed record LecturaRegistrada(
    Guid HospitalizacionId, Guid PacienteId, string CodigoDispositivo, DateTime MedidoEn, SignosValidos Signos, bool Caida);

public interface IManejadorLecturaRegistrada
{
    Task ManejarAsync(LecturaRegistrada lectura, CancellationToken ct = default);
}
