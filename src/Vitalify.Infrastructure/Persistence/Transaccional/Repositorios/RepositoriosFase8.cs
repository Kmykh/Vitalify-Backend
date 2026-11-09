using Microsoft.EntityFrameworkCore;
using Vitalify.Application.Puertos;
using Vitalify.Domain.Nnn;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Repositorios;

internal sealed class RepositorioCatalogoNnn(TransaccionalDbContext db) : IRepositorioCatalogoNnn
{
    public async Task<CatalogoNnnLectura> ObtenerAsync(CancellationToken ct = default) =>
        new(
            await db.DiagnosticosNanda.AsNoTracking().OrderBy(d => d.Codigo).ToListAsync(ct),
            await db.IntervencionesNic.AsNoTracking().OrderBy(i => i.Codigo).ToListAsync(ct),
            await db.ResultadosNoc.AsNoTracking().OrderBy(r => r.Codigo).ToListAsync(ct),
            await db.IndicadoresNoc.AsNoTracking().OrderBy(i => i.Codigo).ToListAsync(ct),
            await db.VinculosNnn.AsNoTracking().ToListAsync(ct));

    public async Task<EntradaCatalogoNnn?> ObtenerParaEditarAsync(TaxonomiaNnn taxonomia, string codigo, CancellationToken ct = default) =>
        taxonomia switch
        {
            TaxonomiaNnn.Nanda => await db.DiagnosticosNanda.SingleOrDefaultAsync(e => e.Codigo == codigo, ct),
            TaxonomiaNnn.Nic => await db.IntervencionesNic.SingleOrDefaultAsync(e => e.Codigo == codigo, ct),
            TaxonomiaNnn.Noc => await db.ResultadosNoc.SingleOrDefaultAsync(e => e.Codigo == codigo, ct),
            TaxonomiaNnn.Indicador => await db.IndicadoresNoc.SingleOrDefaultAsync(e => e.Codigo == codigo, ct),
            _ => null,
        };
}

internal sealed class RepositorioPlanCuidados(TransaccionalDbContext db) : IRepositorioPlanCuidados
{
    public async Task<PlanCuidadosLectura> ObtenerPlanAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        (await LeerAsync(db.DiagnosticosPlan.Where(d => d.HospitalizacionId == hospitalizacionId), ct)) with { HospitalizacionId = hospitalizacionId };

    public async Task<IReadOnlyDictionary<Guid, PlanCuidadosLectura>> ObtenerPlanesActivosAsync(
        IReadOnlyCollection<Guid> hospitalizaciones, CancellationToken ct = default)
    {
        if (hospitalizaciones.Count == 0)
        {
            return new Dictionary<Guid, PlanCuidadosLectura>();
        }

        var todo = await LeerAsync(
            db.DiagnosticosPlan.Where(d => hospitalizaciones.Contains(d.HospitalizacionId) && d.Estado == EstadoDiagnosticoPlan.Activo), ct);
        return hospitalizaciones.ToDictionary(h => h, h =>
        {
            var diagnosticos = todo.Diagnosticos.Where(d => d.HospitalizacionId == h).ToList();
            var ids = diagnosticos.Select(d => d.Id).ToHashSet();
            var intervenciones = todo.Intervenciones.Where(i => ids.Contains(i.PlanDiagnosticoId)).ToList();
            var idsIntervenciones = intervenciones.Select(i => i.Id).ToHashSet();
            var resultados = todo.Resultados.Where(r => ids.Contains(r.PlanDiagnosticoId)).ToList();
            var idsResultados = resultados.Select(r => r.Id).ToHashSet();
            return new PlanCuidadosLectura(
                h, diagnosticos, intervenciones, todo.Ejecuciones.Where(e => idsIntervenciones.Contains(e.PlanIntervencionId)).ToList(),
                resultados, todo.Valoraciones.Where(v => idsResultados.Contains(v.PlanResultadoId)).ToList());
        });
    }

    public async Task<IReadOnlyList<string>> ListarCodigosActivosAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        await db.DiagnosticosPlan.AsNoTracking()
            .Where(d => d.HospitalizacionId == hospitalizacionId && d.Estado == EstadoDiagnosticoPlan.Activo)
            .Select(d => d.DiagnosticoCodigo).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListarCodigosDescartadosDesdeAsync(Guid hospitalizacionId, DateTime desde, CancellationToken ct = default) =>
        await db.DiagnosticosPlan.AsNoTracking()
            .Where(d => d.HospitalizacionId == hospitalizacionId && d.Estado == EstadoDiagnosticoPlan.Descartado && d.CerradoEn >= desde)
            .Select(d => d.DiagnosticoCodigo).Distinct().ToListAsync(ct);

    public Task<bool> ExisteActivoAsync(Guid hospitalizacionId, string diagnosticoCodigo, CancellationToken ct = default) =>
        db.DiagnosticosPlan.AnyAsync(
            d => d.HospitalizacionId == hospitalizacionId && d.DiagnosticoCodigo == diagnosticoCodigo && d.Estado == EstadoDiagnosticoPlan.Activo, ct);

    public Task<DiagnosticoPlan?> ObtenerDiagnosticoAsync(Guid id, CancellationToken ct = default) =>
        db.DiagnosticosPlan.SingleOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<DiagnosticoPlan>> ListarActivosParaCerrarAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        await db.DiagnosticosPlan
            .Where(d => d.HospitalizacionId == hospitalizacionId && d.Estado == EstadoDiagnosticoPlan.Activo)
            .ToListAsync(ct);

    public Task<IntervencionPlan?> ObtenerIntervencionAsync(Guid id, CancellationToken ct = default) =>
        db.IntervencionesPlan.SingleOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<IntervencionPlan>> ObtenerIntervencionesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        ids.Count == 0 ? [] : await db.IntervencionesPlan.Where(i => ids.Contains(i.Id)).ToListAsync(ct);

    public Task<ResultadoPlan?> ObtenerResultadoAsync(Guid id, CancellationToken ct = default) =>
        db.ResultadosPlan.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<SugerenciaEmitida>> ListarSugerenciasPendientesAsync(Guid hospitalizacionId, CancellationToken ct = default) =>
        await db.SugerenciasEmitidas
            .Where(s => s.HospitalizacionId == hospitalizacionId && s.Estado == EstadoSugerencia.Pendiente)
            .ToListAsync(ct);

    public void Agregar(DiagnosticoPlan diagnostico) => db.DiagnosticosPlan.Add(diagnostico);

    public void Agregar(IntervencionPlan intervencion) => db.IntervencionesPlan.Add(intervencion);

    public void Agregar(EjecucionIntervencion ejecucion) => db.EjecucionesIntervencion.Add(ejecucion);

    public void Agregar(ResultadoPlan resultado) => db.ResultadosPlan.Add(resultado);

    public void Agregar(ValoracionResultado valoracion) => db.ValoracionesResultado.Add(valoracion);

    public void Agregar(SugerenciaEmitida sugerencia) => db.SugerenciasEmitidas.Add(sugerencia);

    public async Task<IReadOnlyList<SugerenciaEmitida>> ListarSugerenciasEmitidasEntreAsync(DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        await db.SugerenciasEmitidas.AsNoTracking().Where(s => s.EmitidaEn >= desde && s.EmitidaEn < hasta).ToListAsync(ct);

    public async Task<IReadOnlyList<TiempoAlertaIntervencion>> ListarTiemposAlertaIntervencionAsync(DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        await db.EjecucionesIntervencion.AsNoTracking()
            .Where(e => e.AlertaId != null)
            .Join(db.Alertas.AsNoTracking().Where(a => a.CreadaEn >= desde && a.CreadaEn < hasta), e => e.AlertaId, a => a.Id, (e, a) => new { a.Id, a.CreadaEn, e.EjecutadaEn })
            .GroupBy(x => new { x.Id, x.CreadaEn })
            .Select(g => new TiempoAlertaIntervencion(g.Key.Id, g.Key.CreadaEn, g.Min(x => x.EjecutadaEn)))
            .ToListAsync(ct);

    public Task<PlanCuidadosLectura> ListarConfirmadosActivosEntreAsync(DateTime desde, DateTime hasta, CancellationToken ct = default) =>
        LeerAsync(db.DiagnosticosPlan.Where(d =>
            d.ConfirmadoEn != null && d.ConfirmadoEn < hasta && (d.CerradoEn == null || d.CerradoEn >= desde)), ct);

    /// <summary>Los diagnósticos de la consulta con sus intervenciones, ejecuciones, resultados y valoraciones (5 consultas).</summary>
    private async Task<PlanCuidadosLectura> LeerAsync(IQueryable<DiagnosticoPlan> consulta, CancellationToken ct)
    {
        var diagnosticos = await consulta.AsNoTracking().OrderBy(d => d.ConfirmadoEn ?? d.CerradoEn).ThenBy(d => d.DiagnosticoCodigo).ToListAsync(ct);
        if (diagnosticos.Count == 0)
        {
            return PlanCuidadosLectura.Vacio(Guid.Empty);
        }

        var ids = diagnosticos.Select(d => d.Id).ToList();
        var intervenciones = await db.IntervencionesPlan.AsNoTracking().Where(i => ids.Contains(i.PlanDiagnosticoId)).ToListAsync(ct);
        var idsIntervenciones = intervenciones.Select(i => i.Id).ToList();
        var ejecuciones = await db.EjecucionesIntervencion.AsNoTracking().Where(e => idsIntervenciones.Contains(e.PlanIntervencionId)).ToListAsync(ct);
        var resultados = await db.ResultadosPlan.AsNoTracking().Where(r => ids.Contains(r.PlanDiagnosticoId)).ToListAsync(ct);
        var idsResultados = resultados.Select(r => r.Id).ToList();
        var valoraciones = await db.ValoracionesResultado.AsNoTracking().Where(v => idsResultados.Contains(v.PlanResultadoId)).ToListAsync(ct);
        return new PlanCuidadosLectura(Guid.Empty, diagnosticos, intervenciones, ejecuciones, resultados, valoraciones);
    }
}
