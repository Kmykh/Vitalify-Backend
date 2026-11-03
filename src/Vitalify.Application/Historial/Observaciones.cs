using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Clinica;

namespace Vitalify.Application.Historial;

/// <param name="Pas">Sistólica usada para el puntaje (el promedio de las lecturas).</param>
/// <param name="Lecturas">Cada lectura del tensiómetro, en orden (1 a 3).</param>
public sealed record PresionHistorialDto(
    int? Pas, int? Pad, IReadOnlyList<LecturaPresion>? Lecturas, string? Posicion, string? Brazo, string? Metodo, ChecklistPresion? Checklist);

public sealed record OxigenoHistorialDto(bool? Recibe, string? Dispositivo, decimal? FlujoLpm, int? Fio2, bool? Objetivo88a92);

/// <summary>Una toma manual con todo lo que registró el profesional.</summary>
/// <param name="Conciencia">Nivel ACVPU (<c>Alerta</c>, <c>ConfusionNueva</c>, <c>RespondeVoz</c>, <c>RespondeDolor</c> o <c>NoResponde</c>).</param>
/// <param name="VigenteHasta">Hasta cuándo contaron sus valores para el puntaje (null en las tomas anteriores a la fase 6).</param>
public sealed record ObservacionHistorialDto(
    Guid Id, DateTime ObservadaEn, DateTime RegistradaEn, ProfesionalDto RegistradaPor, int? Fr, decimal? Temperatura, string? Conciencia,
    PresionHistorialDto Presion, OxigenoHistorialDto Oxigeno, ContextoClinico? Contexto, bool? PreocupacionClinica, string? VersionGuia,
    DateTime? VigenteHasta)
{
    public static ObservacionHistorialDto Desde(ObservacionEnfermeria o, ProfesionalDto profesional) =>
        new(o.Id, o.ObservadaEn, o.RegistradaEn, profesional, o.Fr, o.Temperatura, o.Conciencia?.ToString(),
            new PresionHistorialDto(o.Pas, o.Pad, o.LecturasPa, o.PosicionPa?.ToString(), o.BrazoPa?.ToString(), o.MetodoPa?.ToString(), o.ChecklistPa),
            new OxigenoHistorialDto(o.OxigenoSuplementario, o.DispositivoOxigeno?.ToString(), o.FlujoOxigenoLpm, o.Fio2, o.ObjetivoSpo288a92),
            o.Contexto, o.PreocupacionClinica, o.VersionGuia, o.VigenteHasta);
}

public sealed record ObservacionesPacienteDto(
    Guid PacienteId, Guid HospitalizacionId, DateTime Desde, DateTime Hasta, IReadOnlyList<ObservacionHistorialDto> Observaciones);

public sealed record ListarObservacionesConsulta(
    Guid PacienteId, Guid? HospitalizacionId, DateTime? Desde, DateTime? Hasta, Guid UsuarioId, string? Ip);

/// <summary>
/// Tomas manuales del episodio, la más reciente primero, con el nombre del profesional que las registró. Son pocas (una
/// cada 1 a 12 h), así que el rango puede ser el episodio completo.
/// </summary>
public sealed class ListarObservacionesPaciente(
    EpisodiosPaciente episodios, IRepositorioObservaciones observaciones, DirectorioProfesionales directorio,
    AuditoriaHistorial auditoria, IReloj reloj)
{
    public async Task<Resultado<ObservacionesPacienteDto>> EjecutarAsync(ListarObservacionesConsulta consulta, CancellationToken ct = default)
    {
        var elegido = await episodios.ElegirAsync(consulta.PacienteId, consulta.HospitalizacionId, ct);
        if (!elegido.EsExito)
        {
            return elegido.Error;
        }

        var hospitalizacion = elegido.Valor.Hospitalizacion;
        var ventana = EpisodiosPaciente.Ventana(hospitalizacion, reloj.AhoraUtc, consulta.Desde, consulta.Hasta, agrupado: true);
        if (!ventana.EsExito)
        {
            return ventana.Error;
        }

        var filas = await observaciones.ListarAsync(hospitalizacion.Id, ventana.Valor.Desde, ventana.Valor.Hasta, ct);
        var profesionales = await directorio.ObtenerAsync(filas.Select(o => o.RegistradaPor), ct);
        var auditado = await auditoria.RegistrarAsync(consulta.PacienteId, hospitalizacion.Id, "observaciones", consulta.UsuarioId, consulta.Ip, ct);
        return auditado.EsExito
            ? new ObservacionesPacienteDto(consulta.PacienteId, hospitalizacion.Id, ventana.Valor.Desde, ventana.Valor.Hasta,
                filas.Select(o => ObservacionHistorialDto.Desde(o, DirectorioProfesionales.De(profesionales, o.RegistradaPor))).ToList())
            : auditado.Error;
    }
}
