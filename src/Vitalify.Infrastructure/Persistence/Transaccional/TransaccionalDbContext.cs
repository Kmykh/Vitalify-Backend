using Microsoft.EntityFrameworkCore;

namespace Vitalify.Infrastructure.Persistence.Transaccional;

/// <summary>
/// Datos transaccionales (usuarios, pacientes, alertas...) a partir de la fase 1.
/// Usa el esquema <c>vitalify</c> y no <c>public</c>, porque Supabase expone <c>public</c> por su API de datos.
/// </summary>
public class TransaccionalDbContext(DbContextOptions<TransaccionalDbContext> options) : DbContext(options)
{
    public const string Esquema = "vitalify";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TransaccionalDbContext).Assembly,
            t => t.Namespace?.StartsWith(typeof(TransaccionalDbContext).Namespace!, StringComparison.Ordinal) == true);
    }
}
