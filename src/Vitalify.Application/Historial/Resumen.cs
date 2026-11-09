using Vitalify.Application.Alertas;
using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Notas;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Pacientes;

namespace Vitalify.Application.Historial;

public sealed record PacienteResumenDto(
    Guid Id, string NombreCompleto, string TipoDocumento, string NumeroDocumento, DateOnly FechaNacimiento, bool FechaNacimientoEstimada,
    int Edad, bool ConfusionBasal)
{
    public static PacienteResumenDto Desde(Paciente p, DateOnly hoy) =>
        new(p.Id, p.NombreCompleto, p.TipoDocumento.ToString(), p.NumeroDocumento, p.FechaNacimiento, p.FechaNacimientoEstimada, p.Edad(hoy), p.ConfusionBasal);
}

/// <summary>Mínimos y máximos del episodio por variable y por fuente (la temperatura del sensor es de piel; la manual, central).</summary>
public sealed record ExtremosEpisodioDto(ExtremosSignos Sensor, ExtremosSignos TomaManual);

/// <summary>Alertas de un tipo en el episodio, con el tiempo medio de respuesta en segundos desde que se crearon.</summary>
/// <param name="SegundosMedioHastaAtendida">Solo las que cerró una persona (no las resueltas automáticamente).</param>
public sealed record AlertasPorTipoDto(string Tipo, int Cantidad, int Abiertas, double? SegundosMedioHastaTomada, double? SegundosMedioHastaAtendida);

/// <summary>Todo lo que el médico necesita del episodio en una sola respuesta.</summary>
/// <param name="ProximaTomaManualEn">Solo en un episodio activo.</param>
/// <param name="TomaVencida">Pasaron la próxima toma y la tolerancia.</param>
/// <param name="PlanCuidados">Diagnósticos de enfermería activos con su última valoración NOC y sugerencias pendientes (fase 8).</param>
public sealed record ResumenPacienteDto(
    PacienteResumenDto Paciente, EpisodioDto Episodio, EvaluacionRiesgoDto? UltimoRiesgo, ExtremosEpisodioDto Extremos,
    IReadOnlyList<AlertasPorTipoDto> Alertas, IReadOnlyList<AlertaDto> AlertasAbiertas, IReadOnlyList<NotaDto> UltimasNotas,
    DateTime? ProximaTomaManualEn, bool TomaVencida, Nnn.PlanCuidadosResumenDto PlanCuidados);

public sealed record ObtenerResumenConsulta(Guid PacienteId, Guid? HospitalizacionId, Guid UsuarioId, string? Ip);

/// <summary>
/// Resumen del episodio para la vista del médico: paciente y episodio, último riesgo, extremos por variable, alertas
/// por tipo con su tiempo medio de respuesta, alertas abiertas, últimas 5 notas y próxima toma.
/// </summary>
public sealed class ObtenerResumenPaciente(
    EpisodiosPaciente episodios,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioObservaciones observaciones,
    IRepositorioLecturas lecturas,
    IRepositorioAlertas alertas,
    IRepositorioNotas notas,
    PresentadorRiesgo presentadorRiesgo,
    PresentadorNotas presentadorNotas,
    AuditoriaHistorial auditoria,
    OpcionesClinicas opcionesClinicas,
    IReloj reloj,
    Nnn.ResumenPlanCuidados resumenPlan)
{
    public const int NotasEnResumen = 5;

    public async Task<Resultado<ResumenPacienteDto>> EjecutarAsync(ObtenerResumenConsulta consulta, CancellationToken ct = default)
    {
        var elegido = await episodios.ElegirAsync(consulta.PacienteId, consulta.HospitalizacionId, ct);
        if (!elegido.EsExito)
        {
            return elegido.Error;
        }

        var (paciente, hospitalizacion) = (elegido.Valor.Paciente, elegido.Valor.Hospitalizacion);
        var ahora = reloj.AhoraUtc;
        var episodio = (await hospitalizaciones.ListarEpisodiosAsync(paciente.Id, ct)).Single(e => e.HospitalizacionId == hospitalizacion.Id);
        var sensores = await hospitalizaciones.ListarSensoresAsync([hospitalizacion.Id], ct);

        var ultima = await evaluaciones.ObtenerUltimaAsync(hospitalizacion.Id, ct);
        var riesgo = ultima is null ? null : await presentadorRiesgo.PresentarAsync(ultima, ct);

        var tomas = await observaciones.ListarAsync(
            hospitalizacion.Id, ListarObservacionesPaciente.InicioPorDefecto(hospitalizacion), hospitalizacion.EgresoEn ?? ahora, ct);
        var extremos = new ExtremosEpisodioDto(await lecturas.ObtenerExtremosAsync(hospitalizacion.Id, ct), Extremos(tomas));

        var delEpisodio = await alertas.ListarDeHospitalizacionAsync(hospitalizacion.Id, ct);
        var ultimasNotas = await presentadorNotas.PresentarAsync(await notas.ListarDeHospitalizacionAsync(hospitalizacion.Id, NotasEnResumen, ct), ct);

        var planCuidados = await resumenPlan.ObtenerAsync(hospitalizacion, ct);
        var proxima = hospitalizacion.EstaActiva ? riesgo?.ProximaTomaManualEn : null;
        var auditado = await auditoria.RegistrarAsync(paciente.Id, hospitalizacion.Id, "resumen", consulta.UsuarioId, consulta.Ip, ct);
        if (!auditado.EsExito)
        {
            return auditado.Error;
        }

        return new ResumenPacienteDto(
            PacienteResumenDto.Desde(paciente, DateOnly.FromDateTime(ahora)),
            EpisodioDto.Desde(episodio, sensores),
            riesgo,
            extremos,
            PorTipo(delEpisodio.Select(a => a.Alerta).ToList()),
            delEpisodio.Where(a => a.Alerta.EstaAbierta).Select(AlertaDto.Desde).ToList(),
            ultimasNotas,
            proxima,
            proxima is { } p && ahora > p + opcionesClinicas.ToleranciaToma,
            planCuidados);
    }

    /// <summary>Las cinco categorías siempre, aunque no haya alertas de alguna.</summary>
    private static List<AlertasPorTipoDto> PorTipo(IReadOnlyList<AlertaClinica> alertasDelEpisodio) =>
        Enum.GetValues<TipoAlerta>().Select(tipo =>
        {
            var deTipo = alertasDelEpisodio.Where(a => a.Tipo == tipo).ToList();
            var tomadas = deTipo.Where(a => a.TomadaEn is not null).Select(a => (a.TomadaEn!.Value - a.CreadaEn).TotalSeconds).ToList();
            var atendidas = deTipo.Where(a => a.AtendidaPor is not null).Select(a => (a.AtendidaEn!.Value - a.CreadaEn).TotalSeconds).ToList();
            return new AlertasPorTipoDto(
                TextosAlerta.De(tipo), deTipo.Count, deTipo.Count(a => a.EstaAbierta),
                tomadas.Count == 0 ? null : Math.Round(tomadas.Average(), 1),
                atendidas.Count == 0 ? null : Math.Round(atendidas.Average(), 1));
        }).ToList();

    private static ExtremosSignos Extremos(IReadOnlyList<ObservacionEnfermeria> tomas) => new(
        null, Rango(tomas.Select(t => (decimal?)t.Fr)), null, Rango(tomas.Select(t => t.Temperatura)),
        Rango(tomas.Select(t => (decimal?)t.Pas)), Rango(tomas.Select(t => (decimal?)t.Pad)));

    private static RangoValor? Rango(IEnumerable<decimal?> valores)
    {
        var medidos = valores.Where(v => v is not null).Select(v => v!.Value).ToList();
        return medidos.Count == 0 ? null : new RangoValor(medidos.Min(), medidos.Max(), medidos.Count);
    }
}
