using Microsoft.EntityFrameworkCore;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Camas;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Notas;
using Vitalify.Domain.Notificaciones;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Servicios;
using Vitalify.Domain.Sesiones;
using Vitalify.Domain.Turnos;
using Vitalify.Domain.Usuarios;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Infrastructure.Persistence.Transaccional;

/// <summary>
/// Datos transaccionales: usuarios, sesiones, auditoría, camas, dispositivos, pacientes, hospitalizaciones y códigos de vinculación por QR.
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
    public DbSet<CodigoVinculacion> CodigosVinculacion => Set<CodigoVinculacion>();
    public DbSet<Servicio> Servicios => Set<Servicio>();
    public DbSet<ProgramacionTurno> ProgramacionTurnos => Set<ProgramacionTurno>();
    public DbSet<AlertaClinica> Alertas => Set<AlertaClinica>();
    public DbSet<DispositivoMovil> DispositivosMoviles => Set<DispositivoMovil>();
    public DbSet<NotaClinica> Notas => Set<NotaClinica>();
    public DbSet<NotaClinicaVersion> VersionesNotas => Set<NotaClinicaVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(TransaccionalDbContext).Assembly,
            t => t.Namespace?.StartsWith(typeof(TransaccionalDbContext).Namespace!, StringComparison.Ordinal) == true);
        ConvencionSnakeCase.Aplicar(modelBuilder);
    }
}
