using Vitalify.Domain.Nnn;

namespace Vitalify.Application.Puertos;

/// <summary>
/// El catálogo NNN completo (esquema <c>vitalify</c>). Es pequeño (unas decenas de entradas), así que se lee entero y se
/// busca en memoria.
/// </summary>
public interface IRepositorioCatalogoNnn
{
    Task<CatalogoNnnLectura> ObtenerAsync(CancellationToken ct = default);

    /// <summary>Una entrada con seguimiento, para marcarla como verificada.</summary>
    Task<EntradaCatalogoNnn?> ObtenerParaEditarAsync(TaxonomiaNnn taxonomia, string codigo, CancellationToken ct = default);
}

/// <summary>Las tablas del catálogo NNN, con búsquedas por código.</summary>
public sealed class CatalogoNnnLectura(
    IReadOnlyList<DiagnosticoNanda> diagnosticos, IReadOnlyList<IntervencionNic> intervenciones, IReadOnlyList<ResultadoNoc> resultados,
    IReadOnlyList<IndicadorNoc> indicadores, IReadOnlyList<VinculoNnn> vinculos)
{
    private readonly Dictionary<string, DiagnosticoNanda> _diagnosticos = diagnosticos.ToDictionary(d => d.Codigo, StringComparer.Ordinal);
    private readonly Dictionary<string, IntervencionNic> _intervenciones = intervenciones.ToDictionary(i => i.Codigo, StringComparer.Ordinal);
    private readonly Dictionary<string, ResultadoNoc> _resultados = resultados.ToDictionary(r => r.Codigo, StringComparer.Ordinal);
    private readonly Dictionary<string, IndicadorNoc> _indicadores = indicadores.ToDictionary(i => i.Codigo, StringComparer.Ordinal);

    public IReadOnlyList<DiagnosticoNanda> Diagnosticos { get; } = diagnosticos;
    public IReadOnlyList<IntervencionNic> Intervenciones { get; } = intervenciones;
    public IReadOnlyList<ResultadoNoc> Resultados { get; } = resultados;
    public IReadOnlyList<IndicadorNoc> Indicadores { get; } = indicadores;
    public IReadOnlyList<VinculoNnn> Vinculos { get; } = vinculos;

    public DiagnosticoNanda? Diagnostico(string? codigo) => codigo is not null && _diagnosticos.TryGetValue(codigo, out var d) ? d : null;

    public IntervencionNic? Intervencion(string? codigo) => codigo is not null && _intervenciones.TryGetValue(codigo, out var i) ? i : null;

    public ResultadoNoc? Resultado(string? codigo) => codigo is not null && _resultados.TryGetValue(codigo, out var r) ? r : null;

    public IndicadorNoc? Indicador(string? codigo) => codigo is not null && _indicadores.TryGetValue(codigo, out var i) ? i : null;

    public IReadOnlyList<IndicadorNoc> IndicadoresDe(string resultadoCodigo) =>
        Indicadores.Where(i => i.ResultadoCodigo == resultadoCodigo && i.Activo).OrderBy(i => i.Codigo, StringComparer.Ordinal).ToList();

    public IReadOnlyList<VinculoNnn> VinculosDe(string diagnosticoCodigo) =>
        Vinculos.Where(v => v.DiagnosticoCodigo == diagnosticoCodigo).OrderBy(v => v.Tipo).ThenBy(v => v.Orden).ToList();

    /// <summary>La etiqueta de un código de cualquier taxonomía (o el código mismo si no está en el catálogo).</summary>
    public string Etiqueta(TaxonomiaNnn taxonomia, string codigo) => taxonomia switch
    {
        TaxonomiaNnn.Nanda => Diagnostico(codigo)?.Etiqueta,
        TaxonomiaNnn.Nic => Intervencion(codigo)?.Etiqueta,
        TaxonomiaNnn.Noc => Resultado(codigo)?.Etiqueta,
        _ => Indicador(codigo)?.Etiqueta,
    } ?? codigo;
}

/// <summary>El plan de cuidados de una hospitalización, tal como está guardado (sin seguimiento).</summary>
public sealed record PlanCuidadosLectura(
    Guid HospitalizacionId,
    IReadOnlyList<DiagnosticoPlan> Diagnosticos,
    IReadOnlyList<IntervencionPlan> Intervenciones,
    IReadOnlyList<EjecucionIntervencion> Ejecuciones,
    IReadOnlyList<ResultadoPlan> Resultados,
    IReadOnlyList<ValoracionResultado> Valoraciones)
{
    public static PlanCuidadosLectura Vacio(Guid hospitalizacionId) => new(hospitalizacionId, [], [], [], [], []);

    public IReadOnlyList<IntervencionPlan> IntervencionesDe(Guid diagnosticoId) =>
        Intervenciones.Where(i => i.PlanDiagnosticoId == diagnosticoId).OrderBy(i => i.AgregadaEn).ThenBy(i => i.IntervencionCodigo, StringComparer.Ordinal).ToList();

    public IReadOnlyList<EjecucionIntervencion> EjecucionesDe(Guid intervencionId) =>
        Ejecuciones.Where(e => e.PlanIntervencionId == intervencionId).OrderBy(e => e.EjecutadaEn).ToList();

    public IReadOnlyList<ResultadoPlan> ResultadosDe(Guid diagnosticoId) =>
        Resultados.Where(r => r.PlanDiagnosticoId == diagnosticoId).OrderBy(r => r.ResultadoCodigo, StringComparer.Ordinal).ToList();

    public IReadOnlyList<ValoracionResultado> ValoracionesDe(Guid resultadoId) =>
        Valoraciones.Where(v => v.PlanResultadoId == resultadoId).OrderBy(v => v.ValoradaEn).ThenBy(v => v.IndicadorCodigo, StringComparer.Ordinal).ToList();
}

/// <summary>Una alerta y la primera intervención NIC registrada por ella (indicadores de la tesis).</summary>
public sealed record TiempoAlertaIntervencion(Guid AlertaId, DateTime AlertaCreadaEn, DateTime PrimeraIntervencionEn);

/// <summary>Plan de cuidados por hospitalización (esquema <c>vitalify</c>). Se guarda con <see cref="IUnidadDeTrabajo"/>. Nada se borra.</summary>
public interface IRepositorioPlanCuidados
{
    Task<PlanCuidadosLectura> ObtenerPlanAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>Solo los diagnósticos activos de cada hospitalización, con lo suyo (entrega de turno).</summary>
    Task<IReadOnlyDictionary<Guid, PlanCuidadosLectura>> ObtenerPlanesActivosAsync(IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListarCodigosActivosAsync(Guid hospitalizacionId, CancellationToken ct = default);

    /// <summary>Diagnósticos descartados (como sugerencia o ya confirmados) desde ese instante.</summary>
    Task<IReadOnlyList<string>> ListarCodigosDescartadosDesdeAsync(Guid hospitalizacionId, DateTime desde, CancellationToken ct = default);

    Task<bool> ExisteActivoAsync(Guid hospitalizacionId, string diagnosticoCodigo, CancellationToken ct = default);

    Task<DiagnosticoPlan?> ObtenerDiagnosticoAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<DiagnosticoPlan>> ListarActivosParaCerrarAsync(Guid hospitalizacionId, CancellationToken ct = default);

    Task<IntervencionPlan?> ObtenerIntervencionAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<IntervencionPlan>> ObtenerIntervencionesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task<ResultadoPlan?> ObtenerResultadoAsync(Guid id, CancellationToken ct = default);

    /// <summary>Las sugerencias pendientes de la hospitalización, con seguimiento.</summary>
    Task<IReadOnlyList<SugerenciaEmitida>> ListarSugerenciasPendientesAsync(Guid hospitalizacionId, CancellationToken ct = default);

    void Agregar(DiagnosticoPlan diagnostico);

    void Agregar(IntervencionPlan intervencion);

    void Agregar(EjecucionIntervencion ejecucion);

    void Agregar(ResultadoPlan resultado);

    void Agregar(ValoracionResultado valoracion);

    void Agregar(SugerenciaEmitida sugerencia);

    // Indicadores agregados para la tesis (nunca datos de pacientes).
    Task<IReadOnlyList<SugerenciaEmitida>> ListarSugerenciasEmitidasEntreAsync(DateTime desde, DateTime hasta, CancellationToken ct = default);

    Task<IReadOnlyList<TiempoAlertaIntervencion>> ListarTiemposAlertaIntervencionAsync(DateTime desde, DateTime hasta, CancellationToken ct = default);

    /// <summary>Diagnósticos confirmados que estuvieron activos en algún momento del rango, con sus resultados y valoraciones.</summary>
    Task<PlanCuidadosLectura> ListarConfirmadosActivosEntreAsync(DateTime desde, DateTime hasta, CancellationToken ct = default);
}
