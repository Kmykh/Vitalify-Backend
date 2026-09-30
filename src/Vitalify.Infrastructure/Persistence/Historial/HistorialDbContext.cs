using Microsoft.EntityFrameworkCore;

namespace Vitalify.Infrastructure.Persistence.Historial;

/// <summary>
/// Historial de signos vitales y evaluaciones de riesgo (a partir de la fase 3).
/// En la fase 7 se moverá a TimescaleDB: por eso tiene su propia cadena de conexión
/// (<c>ConnectionStrings:Historial</c>) y no debe usar nada propio de Supabase.
/// </summary>
public class HistorialDbContext(DbContextOptions<HistorialDbContext> options) : DbContext(options)
{
    public const string Esquema = "vitalify_historial";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(HistorialDbContext).Assembly,
            t => t.Namespace?.StartsWith(typeof(HistorialDbContext).Namespace!, StringComparison.Ordinal) == true);
        ConvencionSnakeCase.Aplicar(modelBuilder);
    }
}
