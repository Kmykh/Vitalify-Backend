using Microsoft.EntityFrameworkCore;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Infrastructure.Persistence.Historial;

/// <summary>
/// Historial clínico: lecturas, estado actual por hospitalización, incidencias de telemetría, observaciones del
/// personal clínico y evaluaciones de riesgo (NEWS2/MEWS).
/// Vive en Supabase, en su propio esquema, con su propia cadena de conexión (<c>ConnectionStrings:Historial</c>). No usa
/// nada propio de Supabase y no tiene claves foráneas hacia el esquema transaccional (guarda los ids como valores): si
/// algún día crece más de lo que admite el plan, puede pasar a otra base (por ejemplo TimescaleDB) cambiando solo esa
/// cadena.
/// </summary>
public class HistorialDbContext(DbContextOptions<HistorialDbContext> options) : DbContext(options)
{
    public const string Esquema = "vitalify_historial";

    public DbSet<LecturaSignos> Lecturas => Set<LecturaSignos>();
    public DbSet<EstadoSignosActual> EstadosSignos => Set<EstadoSignosActual>();
    public DbSet<IncidenciaTelemetria> Incidencias => Set<IncidenciaTelemetria>();
    public DbSet<ObservacionEnfermeria> Observaciones => Set<ObservacionEnfermeria>();
    public DbSet<EvaluacionRiesgo> Evaluaciones => Set<EvaluacionRiesgo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(HistorialDbContext).Assembly,
            t => t.Namespace?.StartsWith(typeof(HistorialDbContext).Namespace!, StringComparison.Ordinal) == true);
        ConvencionSnakeCase.Aplicar(modelBuilder);
    }
}
