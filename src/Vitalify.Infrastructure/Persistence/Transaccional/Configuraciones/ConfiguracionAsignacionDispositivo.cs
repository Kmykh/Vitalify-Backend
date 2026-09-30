using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionAsignacionDispositivo : IEntityTypeConfiguration<AsignacionDispositivo>
{
    /// <summary>Un dispositivo tiene como máximo una asignación vigente.</summary>
    public const string IndiceDispositivoVigente = "ux_asignacion_dispositivo_vigente";

    /// <summary>Una hospitalización tiene como máximo una asignación vigente (un wearable por paciente).</summary>
    public const string IndiceHospitalizacionVigente = "ux_asignacion_hospitalizacion_vigente";

    private const string SoloVigentes = "liberado_en IS NULL";

    public void Configure(EntityTypeBuilder<AsignacionDispositivo> builder)
    {
        builder.ToTable("asignacion_dispositivo");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.MotivoLiberacion).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(a => a.DispositivoId, IndiceDispositivoVigente).HasDatabaseName(IndiceDispositivoVigente).IsUnique().HasFilter(SoloVigentes);
        builder.HasIndex(a => a.HospitalizacionId, IndiceHospitalizacionVigente).HasDatabaseName(IndiceHospitalizacionVigente).IsUnique().HasFilter(SoloVigentes);
        builder.HasIndex(a => a.DispositivoId);
        builder.HasIndex(a => a.HospitalizacionId);

        builder.HasOne<Dispositivo>().WithMany().HasForeignKey(a => a.DispositivoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hospitalizacion>().WithMany().HasForeignKey(a => a.HospitalizacionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(a => a.AsignadoPor).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(a => a.LiberadoPor).OnDelete(DeleteBehavior.Restrict);
    }
}
