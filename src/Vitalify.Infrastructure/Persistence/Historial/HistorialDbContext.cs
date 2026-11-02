using Microsoft.EntityFrameworkCore;
using Vitalify.Domain.Clinica;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Infrastructure.Persistence.Historial;

/// <summary>
/// Historial clínico: lecturas, estado actual por hospitalización, incidencias de telemetría, observaciones del
/// personal clínico y evaluaciones de riesgo (NEWS2/MEWS).
/// En la fase 7 se moverá a TimescaleDB: por eso tiene su propia cadena de conexión
/// (<c>ConnectionStrings:Historial</c>), no usa nada propio de Supabase y no tiene claves foráneas hacia el
/// esquema transaccional (guarda los ids como valores).
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
