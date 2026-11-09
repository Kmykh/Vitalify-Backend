using Vitalify.Application.Alertas;
using Vitalify.Application.Clinica;
using Vitalify.Application.Comun;
using Vitalify.Application.Puertos;
using Vitalify.Application.Servicios;
using Vitalify.Application.Telemetria;
using Vitalify.Application.Vinculacion;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Telemetria;
using Vitalify.Domain.Turnos;

namespace Vitalify.Application.Turnos;

public sealed record PuntajeEntregaDto(int? Actual, int? Primero, int? Maximo, int? Ultimo);

public sealed record SensorEntregaDto(string Codigo, string Senal, string Conexion, DateTime? UltimaLecturaEn);

public sealed record PreocupacionEntregaDto(DateTime ObservadaEn, Guid RegistradaPor);

/// <summary>Un paciente en la entrega de turno.</summary>
/// <param name="PlanCuidados">Diagnósticos de enfermería activos, intervenciones de cada turno sin ejecutar y valoraciones NOC que faltan (fase 8).</param>
public sealed record PacienteEntregaDto(
    Guid PacienteId, string NombreCompleto, string Cama, string NivelRiesgo, bool? EvaluacionCompleta, PuntajeEntregaDto News2, PuntajeEntregaDto Mews,
    IReadOnlyList<AlertaDto> AlertasAbiertas, IReadOnlyList<AlertaDto> AlertasAtendidasEnElTurno, DateTime? ProximaTomaManualEn, bool TomaVencida,
    SensorEntregaDto? Sensor, PreocupacionEntregaDto? UltimaPreocupacionClinica, Nnn.PlanEntregaDto PlanCuidados);

/// <summary>Resumen del "reporte de enfermería" al cambiar de turno, ordenado por riesgo (el más grave primero).</summary>
public sealed record EntregaTurnoDto(Guid ServicioId, DateTime TurnoDesde, DateTime TurnoHasta, DateTime GeneradaEn, IReadOnlyList<PacienteEntregaDto> Pacientes);

/// <summary>
/// Entrega de turno de un servicio: por cada paciente hospitalizado, NEWS2 y MEWS actuales con su tendencia en la
/// guardia actual (primero, máximo y último), alertas abiertas y atendidas en la guardia, próxima toma manual, estado
/// del sensor y última preocupación clínica. Solo para el personal del servicio.
/// </summary>
public sealed class ObtenerEntregaTurno(
    IRepositorioServicios servicios,
    IRepositorioHospitalizaciones hospitalizaciones,
    IRepositorioEvaluaciones evaluaciones,
    IRepositorioEstadoSignos estados,
    IRepositorioObservaciones observaciones,
    IRepositorioAlertas alertas,
    IPresenciaDispositivos presencia,
    AccesoServicio acceso,
    CatalogoTurnos catalogo,
    OpcionesTelemetria opcionesTelemetria,
    OpcionesClinicas opcionesClinicas,
    IReloj reloj,
    Nnn.PlanEnEntregaTurno planEnEntrega)
{
    public async Task<Resultado<EntregaTurnoDto>> EjecutarAsync(Guid servicioId, Guid usuarioId, CancellationToken ct = default)
    {
        if (await servicios.ObtenerPorIdAsync(servicioId, ct) is null)
        {
            return ErroresServicio.NoEncontrado;
        }

        var usuario = await acceso.UsuarioConServicioAsync(usuarioId, ct);
        if (!usuario.EsExito)
        {
            return usuario.Error;
        }

        if (usuario.Valor.ServicioId != servicioId)
        {
            return ErroresAlerta.OtroServicio;
        }

        // La guardia (diurna o nocturna) que contiene este momento: las dos cubren las 24 h.
        var ahora = reloj.AhoraUtc;
        var guardia = Guardias.Actual(catalogo, ahora).Intervalo;

        var activas = await hospitalizaciones.ListarActivasDeServicioAsync(servicioId, ct);
        var ids = activas.Select(h => h.HospitalizacionId).ToList();
        var ultimas = await evaluaciones.ObtenerUltimasAsync(ids, ct);
        var tendencias = await evaluaciones.ObtenerTendenciasAsync(ids, guardia.InicioUtc, ahora, ct);
        var estadosSignos = await estados.ObtenerVariosAsync(ids, ct);
        var preocupaciones = await observaciones.ObtenerUltimasPreocupacionesAsync(ids, ct);
        var deAlertas = (await alertas.ListarParaEntregaAsync(ids, guardia.InicioUtc, ct)).ToLookup(a => a.Alerta.HospitalizacionId);
        var planes = await planEnEntrega.ObtenerAsync(ids, guardia.InicioUtc, ct);

        var pacientes = activas.Select(h =>
        {
            var evaluacion = ultimas.GetValueOrDefault(h.HospitalizacionId);
            var tendencia = tendencias.GetValueOrDefault(h.HospitalizacionId);
            var estado = estadosSignos.GetValueOrDefault(h.HospitalizacionId);
            var delPaciente = deAlertas[h.HospitalizacionId].ToList();
            var proxima = evaluacion?.ProximaTomaManualEn;
            return new PacienteEntregaDto(
                h.PacienteId, h.NombreCompleto, h.CodigoCama,
                evaluacion is null ? TextosClinicos.SinDatos : TextosClinicos.De(evaluacion.News2Nivel),
                evaluacion is null ? null : evaluacion.News2Completo && evaluacion.MewsCompleto,
                new PuntajeEntregaDto(evaluacion?.News2Total, tendencia?.News2.Primero, tendencia?.News2.Maximo, tendencia?.News2.Ultimo),
                new PuntajeEntregaDto(evaluacion?.MewsTotal, tendencia?.Mews.Primero, tendencia?.Mews.Maximo, tendencia?.Mews.Ultimo),
                delPaciente.Where(a => a.Alerta.EstaAbierta).OrderByDescending(a => a.Alerta.Prioridad).Select(AlertaDto.Desde).ToList(),
                delPaciente.Where(a => !a.Alerta.EstaAbierta).OrderBy(a => a.Alerta.AtendidaEn).Select(AlertaDto.Desde).ToList(),
                proxima,
                proxima is { } p && ahora > p + opcionesClinicas.ToleranciaToma,
                h.CodigoDispositivo is { } codigo
                    ? new SensorEntregaDto(codigo, TextosEstado.De(Vigencia.SenalDe(estado?.UltimaLecturaEn, ahora, opcionesTelemetria.Vigencia)),
                        RegistrarPresenciaDispositivo.Texto(presencia.Obtener(codigo)), estado?.UltimaLecturaEn)
                    : null,
                preocupaciones.GetValueOrDefault(h.HospitalizacionId) is { } o ? new PreocupacionEntregaDto(o.ObservadaEn, o.RegistradaPor) : null,
                planes.GetValueOrDefault(h.HospitalizacionId) ?? Nnn.PlanEntregaDto.Vacio);
        })
        .OrderByDescending(p => AlertaClinica.RangoNivel(p.NivelRiesgo))
        .ThenByDescending(p => p.News2.Actual ?? -1)
        .ThenBy(p => p.Cama, StringComparer.Ordinal)
        .ToList();

        return new EntregaTurnoDto(servicioId, guardia.InicioUtc, guardia.FinUtc, ahora, pacientes);
    }
}
