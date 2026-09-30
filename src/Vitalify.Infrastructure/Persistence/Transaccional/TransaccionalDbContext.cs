using Microsoft.EntityFrameworkCore;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Camas;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Sesiones;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional;

/// <summary>
/// Datos transaccionales: usuarios, sesiones, auditoría, camas, dispositivos, pacientes y hospitalizaciones.
/// Usa el esquema <c>vitalify</c> y no <c>public</c>, porque Supabase expone <c>public</c> por su API de datos.
/// </summary>
public class TransaccionalDbContext(DbContextOptions<TransaccionalDbContext> options) : DbContext(options)
{
    public const string Esquema = "vitalify";

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<SesionRefresco> SesionesRefresco => Set<SesionRefresco>();
    public DbSet<TokenRevocado> TokensRevocados => Set<TokenRevocado>();
    public DbSet<RegistroAuditoria> Auditoria => Set<RegistroAuditoria>();
    public DbSet<Cama> Camas => Set<Cama>();
    public DbSet<Dispositivo> Dispositivos => Set<Dispositivo>();
    public DbSet<Paciente> Pacientes => Set<Paciente>();
    public DbSet<Hospitalizacion> Hospitalizaciones => Set<Hospitalizacion>();
    public DbSet<AsignacionDispositivo> AsignacionesDispositivo => Set<AsignacionDispositivo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TransaccionalDbContext).Assembly,
            t => t.Namespace?.StartsWith(typeof(TransaccionalDbContext).Namespace!, StringComparison.Ordinal) == true);
        ConvencionSnakeCase.Aplicar(modelBuilder);
    }
}
