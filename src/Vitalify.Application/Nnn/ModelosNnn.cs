using Vitalify.Application.Historial;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Nnn;

namespace Vitalify.Application.Nnn;

/// <summary>Una entrada del catálogo: código, etiqueta corta, edición y si una enfermera asesora ya la verificó.</summary>
public sealed record EntradaCatalogoDto(string Codigo, string Etiqueta, string Edicion, bool Verificado)
{
    public static EntradaCatalogoDto Desde(EntradaCatalogoNnn e) => new(e.Codigo, e.Etiqueta, e.Edicion, e.Verificado);
}

/// <param name="Tipo"><c>Real</c>, <c>Riesgo</c> o <c>PromocionSalud</c>.</param>
public sealed record DiagnosticoCatalogoDto(string Codigo, string Etiqueta, string? Dominio, string? Clase, string Tipo, string Edicion, bool Verificado)
{
    public static DiagnosticoCatalogoDto Desde(DiagnosticoNanda d) => new(d.Codigo, d.Etiqueta, d.Dominio, d.Clase, d.Tipo.ToString(), d.Edicion, d.Verificado);
}

/// <param name="ParametroVital"><c>fr</c>, <c>spo2</c>, <c>temperatura</c>, <c>pas</c>, <c>fc</c> o <c>conciencia</c>; null si no se valora con los signos.</param>
public sealed record IndicadorDto(string Codigo, string Etiqueta, string? ParametroVital, bool Verificado)
{
    public static IndicadorDto Desde(IndicadorNoc i) => new(i.Codigo, i.Etiqueta, TextosNnn.De(i.ParametroVital), i.Verificado);
}

public sealed record ResultadoCatalogoDto(string Codigo, string Etiqueta, string Escala, string Edicion, bool Verificado, IReadOnlyList<IndicadorDto> Indicadores);

/// <summary>NIC y NOC sugeridos para un diagnóstico, en el orden del catálogo.</summary>
public sealed record VinculosDiagnosticoDto(
    DiagnosticoCatalogoDto Diagnostico, IReadOnlyList<EntradaCatalogoDto> Intervenciones, IReadOnlyList<ResultadoCatalogoDto> Resultados);

/// <summary>Un código del catálogo con su etiqueta, para mostrar sin otra consulta.</summary>
public sealed record CodigoEtiquetaDto(string Codigo, string Etiqueta, bool Verificado);

/// <summary>
/// Una sugerencia del motor: diagnóstico NANDA-I con sus NIC y NOC, la regla que la disparó y el motivo legible. Es apoyo a la
/// decisión: la enfermera la confirma (<c>POST .../plan-cuidados/diagnosticos</c>) o la descarta con motivo.
/// </summary>
/// <param name="Prioridad"><c>alta</c>, <c>media</c> o <c>baja</c>.</param>
/// <param name="PorConfirmar">Se apoya solo en el wearable (sensor no certificado): conviene una toma manual de <paramref name="ConfirmarCon"/> antes de confirmarla.</param>
/// <param name="ConfirmarCon">Los signos a tomar a mano (<c>fr</c>, <c>spo2</c>, <c>temperatura</c>, <c>pas</c>, <c>fc</c>…).</param>
/// <param name="Retenida">El criterio ya no se cumple, pero sigue pendiente 30 min (histéresis) antes de expirar.</param>
/// <param name="Paquete">Lo que se planifica si la enfermera la confirma con un toque (fase 9).</param>
public sealed record SugerenciaNnnDto(
    CodigoEtiquetaDto Diagnostico, IReadOnlyList<CodigoEtiquetaDto> Intervenciones, IReadOnlyList<CodigoEtiquetaDto> Resultados,
    string Regla, IReadOnlyList<string> Reglas, string Motivo, string Prioridad, Guid? EvaluacionId, Guid? AlertaId,
    bool PorConfirmar = false, IReadOnlyList<string>? ConfirmarCon = null, bool Retenida = false, PaqueteNnnDto? Paquete = null);

/// <param name="Frecuencia"><c>UnaVez</c>, <c>CadaTurno</c>, <c>CadaHora</c>, <c>SegunNecesidad</c> o <c>PorProtocolo</c>.</param>
public sealed record PaqueteIntervencionDto(string Codigo, string Etiqueta, bool Verificado, string Frecuencia);

/// <param name="BasalDesdeSignos">La basal salió de los signos (mediana de 30 min) o del dolor; si no, es 3 por defecto y la enfermera debe revisarla.</param>
public sealed record PaqueteResultadoDto(string Codigo, string Etiqueta, bool Verificado, int PuntuacionBasal, int PuntuacionMeta, bool BasalDesdeSignos);

/// <summary>El "paquete listo" de una sugerencia: NIC con su frecuencia por protocolo y NOC con basal y meta.</summary>
public sealed record PaqueteNnnDto(IReadOnlyList<PaqueteIntervencionDto> Intervenciones, IReadOnlyList<PaqueteResultadoDto> Resultados, string VersionProtocolo);

/// <param name="Aviso">Siempre "Vitalify sugiere; la enfermera decide."</param>
public sealed record SugerenciasPacienteDto(
    Guid PacienteId, Guid HospitalizacionId, string VersionReglas, Guid? EvaluacionId, DateTime? EvaluadaEn, int? News2, bool? EvaluacionCompleta,
    IReadOnlyList<SugerenciaNnnDto> Sugerencias, string Aviso);

public sealed record SugerenciasAlertaDto(
    Guid AlertaId, Guid PacienteId, Guid HospitalizacionId, string VersionReglas, Guid? EvaluacionId, DateTime? EvaluadaEn,
    IReadOnlyList<SugerenciaNnnDto> Sugerencias, string Aviso);

/// <param name="Origen"><c>Manual</c>, <c>TomaManual</c> (la NIC 6680 que registró una toma completa), <c>Alerta</c> o <c>Lote</c>.</param>
public sealed record EjecucionDto(
    Guid Id, Guid PlanIntervencionId, ProfesionalDto EjecutadaPor, DateTime EjecutadaEn, Guid? AlertaId, string? Observacion, string Origen = "Manual");

/// <param name="Frecuencia"><c>UnaVez</c>, <c>CadaTurno</c>, <c>CadaHora</c>, <c>SegunNecesidad</c> o <c>PorProtocolo</c> (según el NEWS2).</param>
/// <param name="ProximaEn">Cuándo le toca (solo en el plan de la hospitalización activa).</param>
/// <param name="Agenda"><c>al-dia</c>, <c>proxima</c>, <c>vencida</c> o <c>segun-necesidad</c>.</param>
public sealed record IntervencionPlanDto(
    Guid Id, string Codigo, string Etiqueta, bool Verificado, string Frecuencia, bool Activa, ProfesionalDto AgregadaPor, DateTime AgregadaEn,
    DateTime? UltimaEjecucionEn, IReadOnlyList<EjecucionDto> Ejecuciones, DateTime? ProximaEn = null, string? Agenda = null);

/// <param name="IndicadorCodigo">Null si se valoró el resultado completo.</param>
/// <param name="PuntuacionSugerida">Lo que propuso el motor con los puntos NEWS2 de ese signo (solo indicadores vitales).</param>
public sealed record ValoracionDto(
    Guid Id, string? IndicadorCodigo, string Etiqueta, int Puntuacion, int? PuntuacionSugerida, ProfesionalDto ValoradaPor, DateTime ValoradaEn, string? Turno);

/// <param name="UltimaPuntuacion">Promedio de la última valoración (todas las del mismo momento); null si no se valoró.</param>
public sealed record ResultadoPlanDto(
    Guid Id, string Codigo, string Etiqueta, bool Verificado, string Escala, int PuntuacionBasal, int PuntuacionMeta, double? UltimaPuntuacion,
    DateTime? UltimaValoracionEn, IReadOnlyList<IndicadorDto> Indicadores, IReadOnlyList<ValoracionDto> Valoraciones);

/// <param name="Estado"><c>Activo</c>, <c>Resuelto</c> o <c>Descartado</c>.</param>
/// <param name="Origen"><c>Sugerido</c> o <c>Manual</c>.</param>
public sealed record DiagnosticoPlanDto(
    Guid Id, Guid PacienteId, Guid HospitalizacionId, string Codigo, string Etiqueta, string Tipo, bool Verificado, string Origen,
    string? ReglaOrigen, Guid? AlertaId, Guid? EvaluacionId, string Estado, string? MotivoCierre, ProfesionalDto? ConfirmadoPor,
    DateTime? ConfirmadoEn, ProfesionalDto? CerradoPor, DateTime? CerradoEn, string? VersionReglas,
    IReadOnlyList<IntervencionPlanDto> Intervenciones, IReadOnlyList<ResultadoPlanDto> Resultados,
    bool? ConfirmadoSinCambios = null, string? VersionProtocolo = null, string? ListoParaResolver = null);

/// <summary>El plan de cuidados completo de un episodio: cada diagnóstico con sus intervenciones, ejecuciones, resultados y la serie de valoraciones.</summary>
/// <param name="NivelRespuesta">Nivel de respuesta NEWS2 con que se agenda (<c>cero</c>, <c>bajo</c>, <c>un-parametro-en-3</c>, <c>medio</c> o <c>alto</c>); null si el episodio terminó.</param>
public sealed record PlanCuidadosDto(
    Guid PacienteId, Guid HospitalizacionId, bool HospitalizacionActiva, string VersionReglas, IReadOnlyList<DiagnosticoPlanDto> Diagnosticos,
    string? NivelRespuesta = null, string VersionProtocolo = ProtocoloNnn.Version);

/// <summary>Lo que la agenda y la propuesta de cierre agregan al plan de la hospitalización activa.</summary>
public sealed record ExtrasPlan(IReadOnlyDictionary<Guid, ItemAgenda> Agenda, IReadOnlyDictionary<Guid, string> ListosParaResolver)
{
    public static readonly ExtrasPlan Ninguno = new(new Dictionary<Guid, ItemAgenda>(), new Dictionary<Guid, string>());
}

public sealed record ValoracionRegistradaDto(Guid PlanResultadoId, DateTime ValoradaEn, string? Turno, IReadOnlyList<ValoracionDto> Indicadores);

/// <summary>Un indicador del resultado con la puntuación que propone el motor, para mostrarla antes de valorar.</summary>
/// <param name="Codigo">El indicador, o el código del resultado para valorarlo completo.</param>
public sealed record IndicadorSugeridoDto(string Codigo, string Etiqueta, string? ParametroVital, int? PuntuacionSugerida, int? UltimaPuntuacion);

public sealed record ValoracionSugeridaDto(
    Guid PlanResultadoId, string ResultadoCodigo, string Etiqueta, string Escala, Guid? EvaluacionId, DateTime? EvaluadaEn,
    IReadOnlyList<IndicadorSugeridoDto> Indicadores);

/// <summary>La última valoración NOC de un diagnóstico (el resultado valorado más recientemente).</summary>
public sealed record UltimaValoracionDto(string ResultadoCodigo, string ResultadoEtiqueta, double Puntuacion, int PuntuacionMeta, DateTime ValoradaEn);

public sealed record DiagnosticoResumenDto(Guid Id, string Codigo, string Etiqueta, DateTime? ConfirmadoEn, UltimaValoracionDto? UltimaValoracion);

/// <summary>El plan de cuidados en el resumen del paciente.</summary>
public sealed record PlanCuidadosResumenDto(IReadOnlyList<DiagnosticoResumenDto> DiagnosticosActivos, int SugerenciasPendientes)
{
    public static readonly PlanCuidadosResumenDto Vacio = new([], 0);
}

public sealed record DiagnosticoEntregaDto(Guid Id, string Codigo, string Etiqueta);

public sealed record IntervencionPendienteDto(Guid Id, Guid PlanDiagnosticoId, string Codigo, string Etiqueta, string DiagnosticoCodigo);

public sealed record ValoracionPendienteDto(Guid PlanResultadoId, Guid PlanDiagnosticoId, string Codigo, string Etiqueta, string DiagnosticoCodigo);

/// <summary>El plan en la entrega de turno: diagnósticos activos, intervenciones de cada turno aún no ejecutadas y valoraciones NOC que faltan en el turno.</summary>
public sealed record PlanEntregaDto(
    IReadOnlyList<DiagnosticoEntregaDto> DiagnosticosActivos, IReadOnlyList<IntervencionPendienteDto> IntervencionesPendientes,
    IReadOnlyList<ValoracionPendienteDto> ValoracionesPendientes)
{
    public static readonly PlanEntregaDto Vacio = new([], [], []);
}

public static class TextosNnn
{
    public const string Aviso = "Vitalify sugiere; la enfermera decide.";

    public static string? De(ParametroVital? p) => p switch
    {
        ParametroVital.Fr => "fr",
        ParametroVital.Spo2 => "spo2",
        ParametroVital.Temperatura => "temperatura",
        ParametroVital.Pas => "pas",
        ParametroVital.Fc => "fc",
        ParametroVital.Conciencia => "conciencia",
        _ => null,
    };

    public static string De(EstadoAgenda e) => e switch
    {
        EstadoAgenda.Vencida => "vencida",
        EstadoAgenda.Proxima => "proxima",
        EstadoAgenda.SegunNecesidad => "segun-necesidad",
        _ => "al-dia",
    };

    public static string De(NivelRespuestaNews2 n) => n switch
    {
        NivelRespuestaNews2.Alto => "alto",
        NivelRespuestaNews2.Medio => "medio",
        NivelRespuestaNews2.UnParametroEnTres => "un-parametro-en-3",
        NivelRespuestaNews2.Bajo => "bajo",
        _ => "cero",
    };

    public static string De(ParametroClinico p) => p switch
    {
        ParametroClinico.Fr => "fr",
        ParametroClinico.Spo2 => "spo2",
        ParametroClinico.OxigenoSuplementario => "oxigeno",
        ParametroClinico.Pas => "pas",
        ParametroClinico.Fc => "fc",
        ParametroClinico.Conciencia => "conciencia",
        _ => "temperatura",
    };

    public static string De(PrioridadSugerencia p) => p switch
    {
        PrioridadSugerencia.Alta => "alta",
        PrioridadSugerencia.Media => "media",
        _ => "baja",
    };

    /// <summary><c>nanda</c>, <c>nic</c>, <c>noc</c> o <c>indicador</c>.</summary>
    public static bool TryTaxonomia(string? texto, out TaxonomiaNnn taxonomia)
    {
        taxonomia = (texto ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "nanda" => TaxonomiaNnn.Nanda,
            "nic" => TaxonomiaNnn.Nic,
            "noc" => TaxonomiaNnn.Noc,
            "indicador" => TaxonomiaNnn.Indicador,
            _ => 0,
        };
        return taxonomia != 0;
    }
}

/// <summary>Arma los DTO del plan con las etiquetas del catálogo y los nombres del personal.</summary>
public sealed class PresentadorPlanCuidados(IRepositorioCatalogoNnn catalogo, DirectorioProfesionales directorio)
{
    public Task<CatalogoNnnLectura> CatalogoAsync(CancellationToken ct = default) => catalogo.ObtenerAsync(ct);

    public static CodigoEtiquetaDto Codigo(CatalogoNnnLectura c, TaxonomiaNnn taxonomia, string codigo)
    {
        EntradaCatalogoNnn? e = taxonomia switch
        {
            TaxonomiaNnn.Nanda => c.Diagnostico(codigo),
            TaxonomiaNnn.Nic => c.Intervencion(codigo),
            TaxonomiaNnn.Noc => c.Resultado(codigo),
            _ => c.Indicador(codigo),
        };
        return new CodigoEtiquetaDto(codigo, e?.Etiqueta ?? codigo, e?.Verificado ?? false);
    }

    public static SugerenciaNnnDto Sugerencia(CatalogoNnnLectura c, SugerenciaNnn s, PaqueteNnnDto? paquete = null) =>
        new(Codigo(c, TaxonomiaNnn.Nanda, s.DiagnosticoCodigo),
            s.Intervenciones.Select(i => Codigo(c, TaxonomiaNnn.Nic, i)).ToList(),
            s.Resultados.Select(r => Codigo(c, TaxonomiaNnn.Noc, r)).ToList(),
            s.Regla, s.Reglas, s.Motivo, TextosNnn.De(s.Prioridad), s.EvaluacionId, s.AlertaId,
            s.PorConfirmar, s.ConfirmarCon?.Select(TextosNnn.De).ToList(), s.Retenida, paquete);

    public Task<IReadOnlyList<DiagnosticoPlanDto>> PresentarAsync(PlanCuidadosLectura plan, CancellationToken ct = default) =>
        PresentarAsync(plan, ExtrasPlan.Ninguno, ct);

    public async Task<IReadOnlyList<DiagnosticoPlanDto>> PresentarAsync(PlanCuidadosLectura plan, ExtrasPlan extras, CancellationToken ct = default)
    {
        var c = await catalogo.ObtenerAsync(ct);
        var personas = await directorio.ObtenerAsync(
            plan.Diagnosticos.SelectMany(d => new[] { d.ConfirmadoPor, d.CerradoPor }).OfType<Guid>()
                .Concat(plan.Intervenciones.Select(i => i.AgregadaPor))
                .Concat(plan.Ejecuciones.Select(e => e.EjecutadaPor))
                .Concat(plan.Valoraciones.Select(v => v.ValoradaPor)), ct);
        return plan.Diagnosticos.Select(d => Diagnostico(c, personas, plan, d, extras)).ToList();
    }

    public static DiagnosticoPlanDto Diagnostico(
        CatalogoNnnLectura c, IReadOnlyDictionary<Guid, ProfesionalDto> personas, PlanCuidadosLectura plan, DiagnosticoPlan d, ExtrasPlan? extras = null)
    {
        var nanda = c.Diagnostico(d.DiagnosticoCodigo);
        return new DiagnosticoPlanDto(
            d.Id, d.PacienteId, d.HospitalizacionId, d.DiagnosticoCodigo, nanda?.Etiqueta ?? d.DiagnosticoCodigo, nanda?.Tipo.ToString() ?? string.Empty,
            nanda?.Verificado ?? false, d.Origen.ToString(), d.ReglaOrigen, d.AlertaId, d.EvaluacionId, d.Estado.ToString(), d.MotivoCierre,
            d.ConfirmadoPor is { } cp ? DirectorioProfesionales.De(personas, cp) : null, d.ConfirmadoEn,
            d.CerradoPor is { } ce ? DirectorioProfesionales.De(personas, ce) : null, d.CerradoEn, d.VersionReglas,
            plan.IntervencionesDe(d.Id).Select(i => Intervencion(c, personas, plan, i, extras?.Agenda.GetValueOrDefault(i.Id))).ToList(),
            plan.ResultadosDe(d.Id).Select(r => Resultado(c, personas, plan, r)).ToList(),
            d.ConfirmadoSinCambios, d.VersionProtocolo, extras?.ListosParaResolver.GetValueOrDefault(d.Id));
    }

    public static IntervencionPlanDto Intervencion(
        CatalogoNnnLectura c, IReadOnlyDictionary<Guid, ProfesionalDto> personas, PlanCuidadosLectura plan, IntervencionPlan i, ItemAgenda? agenda = null)
    {
        var nic = c.Intervencion(i.IntervencionCodigo);
        var ejecuciones = plan.EjecucionesDe(i.Id).Select(e => Ejecucion(personas, e)).ToList();
        return new IntervencionPlanDto(
            i.Id, i.IntervencionCodigo, nic?.Etiqueta ?? i.IntervencionCodigo, nic?.Verificado ?? false, i.Frecuencia.ToString(), i.Activa,
            DirectorioProfesionales.De(personas, i.AgregadaPor), i.AgregadaEn, ejecuciones.LastOrDefault()?.EjecutadaEn, ejecuciones,
            agenda?.ProximaEn, agenda is null ? null : TextosNnn.De(agenda.Estado));
    }

    public static EjecucionDto Ejecucion(IReadOnlyDictionary<Guid, ProfesionalDto> personas, EjecucionIntervencion e) =>
        new(e.Id, e.PlanIntervencionId, DirectorioProfesionales.De(personas, e.EjecutadaPor), e.EjecutadaEn, e.AlertaId, e.Observacion, e.Origen.ToString());

    public static ResultadoPlanDto Resultado(
        CatalogoNnnLectura c, IReadOnlyDictionary<Guid, ProfesionalDto> personas, PlanCuidadosLectura plan, ResultadoPlan r)
    {
        var noc = c.Resultado(r.ResultadoCodigo);
        var valoraciones = plan.ValoracionesDe(r.Id);
        var ultima = Ultima(valoraciones);
        return new ResultadoPlanDto(
            r.Id, r.ResultadoCodigo, noc?.Etiqueta ?? r.ResultadoCodigo, noc?.Verificado ?? false, noc?.Escala ?? string.Empty,
            r.PuntuacionBasal, r.PuntuacionMeta, ultima?.Puntuacion, ultima?.En,
            c.IndicadoresDe(r.ResultadoCodigo).Select(IndicadorDto.Desde).ToList(),
            valoraciones.Select(v => Valoracion(c, personas, r, v)).ToList());
    }

    public static ValoracionDto Valoracion(CatalogoNnnLectura c, IReadOnlyDictionary<Guid, ProfesionalDto> personas, ResultadoPlan r, ValoracionResultado v) =>
        new(v.Id, v.IndicadorCodigo,
            v.IndicadorCodigo is { } i ? c.Indicador(i)?.Etiqueta ?? i : $"{c.Resultado(r.ResultadoCodigo)?.Etiqueta ?? r.ResultadoCodigo} (global)",
            v.Puntuacion, v.PuntuacionSugerida, DirectorioProfesionales.De(personas, v.ValoradaPor), v.ValoradaEn, v.Turno);

    /// <summary>Promedio de la última valoración (las del mismo instante) de un resultado.</summary>
    public static (double Puntuacion, DateTime En)? Ultima(IReadOnlyCollection<ValoracionResultado> valoraciones)
    {
        if (valoraciones.Count == 0)
        {
            return null;
        }

        var en = valoraciones.Max(v => v.ValoradaEn);
        return (Math.Round(valoraciones.Where(v => v.ValoradaEn == en).Average(v => v.Puntuacion), 2), en);
    }
}
