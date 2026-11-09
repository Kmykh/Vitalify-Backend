using Vitalify.Application.Puertos;
using Vitalify.Domain.Nnn;

namespace Vitalify.Application.Tests.Fakes;

/// <summary>Catálogo NNN en memoria con algunas entradas de la semilla.</summary>
public sealed class RepositorioCatalogoNnnEnMemoria : IRepositorioCatalogoNnn
{
    private const string Nanda = "NANDA-I 2024-2026";
    private const string Nic = "NIC 8a ed.";
    private const string Noc = "NOC 7a ed.";

    public List<DiagnosticoNanda> Diagnosticos { get; } =
    [
        DiagnosticoNanda.Crear("00032", "Patrón respiratorio ineficaz", "Actividad/reposo", null, TipoDiagnosticoNanda.Real, Nanda),
        DiagnosticoNanda.Crear("00030", "Deterioro del intercambio de gases", null, null, TipoDiagnosticoNanda.Real, Nanda),
        DiagnosticoNanda.Crear("00303", "Riesgo de caídas del adulto", null, null, TipoDiagnosticoNanda.Riesgo, Nanda),
    ];

    public List<IntervencionNic> Intervenciones { get; } =
    [
        IntervencionNic.Crear("3350", "Monitorización respiratoria", null, Nic),
        IntervencionNic.Crear("3140", "Manejo de la vía aérea", null, Nic),
        IntervencionNic.Crear("6490", "Prevención de caídas", null, Nic),
        IntervencionNic.Crear("7710", "Colaboración con el médico", null, Nic),
    ];

    public List<ResultadoNoc> Resultados { get; } =
    [
        ResultadoNoc.Crear("0415", "Estado respiratorio", "Desviación grave (1) a Sin desviación (5)", Noc),
        ResultadoNoc.Crear("0802", "Signos vitales", "Desviación grave (1) a Sin desviación (5)", Noc),
    ];

    public List<IndicadorNoc> Indicadores { get; } = [IndicadorNoc.Crear("080204", "0802", "Frecuencia respiratoria", ParametroVital.Fr, Noc)];

    public Task<CatalogoNnnLectura> ObtenerAsync(CancellationToken ct = default) =>
        Task.FromResult(new CatalogoNnnLectura(Diagnosticos, Intervenciones, Resultados, Indicadores, []));

    public Task<EntradaCatalogoNnn?> ObtenerParaEditarAsync(TaxonomiaNnn taxonomia, string codigo, CancellationToken ct = default) =>
        Task.FromResult<EntradaCatalogoNnn?>(taxonomia switch
        {
            TaxonomiaNnn.Nanda => Diagnosticos.SingleOrDefault(d => d.Codigo == codigo),
            TaxonomiaNnn.Nic => Intervenciones.SingleOrDefault(d => d.Codigo == codigo),
            TaxonomiaNnn.Noc => Resultados.SingleOrDefault(d => d.Codigo == codigo),
            _ => Indicadores.SingleOrDefault(d => d.Codigo == codigo),
        });
}

/// <summary>Plan de cuidados en memoria.</summary>
public sealed class RepositorioPlanCuidadosEnMemoria : IRepositorioPlanCuidados
{
    public List<DiagnosticoPlan> Diagnosticos { get; } = [];
    public List<IntervencionPlan> Intervenciones { get; } = [];
    public List<EjecucionIntervencion> Ejecuciones { get; } = [];
    public List<ResultadoPlan> Resultados { get; } = [];
    public List<ValoracionResultado> Valoraciones { get; } = [];
    public List<SugerenciaEmitida> Sugerencias { get; } = [];

    public Task<PlanCuidadosLectura> ObtenerPlanAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        Task.FromResult(Leer(hospitalizacionId, Diagnosticos.Where(d => d.HospitalizacionId == hospitalizacionId)));

    public Task<IReadOnlyDictionary<Guid, PlanCuidadosLectura>> ObtenerPlanesActivosAsync(IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PlanCuidadosLectura>>(hospitalizaciones.ToDictionary(
            h => h, h => Leer(h, Diagnosticos.Where(d => d.HospitalizacionId == h && d.EstaActivo))));

    public Task<IReadOnlyList<string>> ListarCodigosActivosAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Diagnosticos.Where(d => d.HospitalizacionId == hospitalizacionId && d.EstaActivo).Select(d => d.DiagnosticoCodigo).ToList());

    public Task<IReadOnlyList<string>> ListarCodigosDescartadosDesdeAsync(Guid hospitalizacionId, DateTime desde, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Diagnosticos
            .Where(d => d.HospitalizacionId == hospitalizacionId && d.Estado == EstadoDiagnosticoPlan.Descartado && d.CerradoEn >= desde)
            .Select(d => d.DiagnosticoCodigo).Distinct().ToList());

    public Task<bool> ExisteActivoAsync(Guid hospitalizacionId, string diagnosticoCodigo, CancellationToken ct = default) =>
        Task.FromResult(Diagnosticos.Any(d => d.HospitalizacionId == hospitalizacionId && d.DiagnosticoCodigo == diagnosticoCodigo && d.EstaActivo));

    public Task<DiagnosticoPlan?> ObtenerDiagnosticoAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Diagnosticos.SingleOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<DiagnosticoPlan>> ListarActivosParaCerrarAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DiagnosticoPlan>>(Diagnosticos.Where(d => d.HospitalizacionId == hospitalizacionId && d.EstaActivo).ToList());

    public Task<IntervencionPlan?> ObtenerIntervencionAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Intervenciones.SingleOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<IntervencionPlan>> ObtenerIntervencionesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IntervencionPlan>>(Intervenciones.Where(i => ids.Contains(i.Id)).ToList());

    public Task<ResultadoPlan?> ObtenerResultadoAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Resultados.SingleOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<SugerenciaEmitida>> ListarSugerenciasPendientesAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SugerenciaEmitida>>(Sugerencias.Where(s => s.HospitalizacionId == hospitalizacionId && s.EstaPendiente).ToList());

    public void Agregar(DiagnosticoPlan diagnostico) => Diagnosticos.Add(diagnostico);

    public void Agregar(IntervencionPlan intervencion) => Intervenciones.Add(intervencion);

    public void Agregar(EjecucionIntervencion ejecucion) => Ejecuciones.Add(ejecucion);

    public void Agregar(ResultadoPlan resultado) => Resultados.Add(resultado);

    public void Agregar(ValoracionResultado valoracion) => Valoraciones.Add(valoracion);

    public void Agregar(SugerenciaEmitida sugerencia) => Sugerencias.Add(sugerencia);

    public Task<IReadOnlyList<SugerenciaEmitida>> ListarSugerenciasEmitidasEntreAsync(DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SugerenciaEmitida>>(Sugerencias.Where(s => s.EmitidaEn >= desde && s.EmitidaEn < hasta).ToList());

    public Task<IReadOnlyList<TiempoAlertaIntervencion>> ListarTiemposAlertaIntervencionAsync(DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TiempoAlertaIntervencion>>([]);

    public Task<PlanCuidadosLectura> ListarConfirmadosActivosEntreAsync(DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        Task.FromResult(Leer(Guid.Empty, Diagnosticos.Where(d => d.ConfirmadoEn < hasta && (d.CerradoEn == null || d.CerradoEn >= desde))));

    private PlanCuidadosLectura Leer(Guid hospitalizacionId, IEnumerable<DiagnosticoPlan> diagnosticos)
    {
        var d = diagnosticos.ToList();
        var ids = d.Select(x => x.Id).ToHashSet();
        var intervenciones = Intervenciones.Where(i => ids.Contains(i.PlanDiagnosticoId)).ToList();
        var resultados = Resultados.Where(r => ids.Contains(r.PlanDiagnosticoId)).ToList();
        return new PlanCuidadosLectura(
            hospitalizacionId, d, intervenciones, Ejecuciones.Where(e => intervenciones.Any(i => i.Id == e.PlanIntervencionId)).ToList(),
            resultados, Valoraciones.Where(v => resultados.Any(r => r.Id == v.PlanResultadoId)).ToList());
    }
}
