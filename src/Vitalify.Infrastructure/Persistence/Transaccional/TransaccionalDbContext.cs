using Microsoft.EntityFrameworkCore;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Sesiones;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional;

/// <summary>
/// Datos transaccionales (usuarios, sesiones, auditoría; luego pacientes, alertas...).
/// Usa el esquema <c>vitalify</c> y no <c>public</c>, porque Supabase expone <c>public</c> por su API de datos.
/// </summary>
public class TransaccionalDbContext(DbContextOptions<TransaccionalDbContext> options) : DbContext(options)
{
    public const string Esquema = "vitalify";

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<SesionRefresco> SesionesRefresco => Set<SesionRefresco>();
    public DbSet<TokenRevocado> TokensRevocados => Set<TokenRevocado>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TransaccionalDbContext).Assembly,
            t => t.Namespace?.StartsWith(typeof(TransaccionalDbContext).Namespace!, StringComparison.Ordinal) == true);
        ConvencionSnakeCase.Aplicar(modelBuilder);
    }
}
