using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Camas;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionHospitalizacion : IEntityTypeConfiguration<Hospitalizacion>
{
    /// <summary>Una cama tiene como máximo una hospitalización activa.</summary>
    public const string IndiceCamaActiva = "ux_hospitalizacion_cama_activa";

    /// <summary>Un paciente tiene como máximo una hospitalización activa.</summary>
    public const string IndicePacienteActivo = "ux_hospitalizacion_paciente_activa";

    private const string SoloActivas = "estado = 'Activa'";

    public void Configure(EntityTypeBuilder<Hospitalizacion> builder)
    {
        builder.ToTable("hospitalizacion");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.DiagnosticoIngreso).HasMaxLength(Hospitalizacion.LargoMaximoDiagnostico).IsRequired();
        builder.Property(h => h.MotivoEgreso).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ObservacionEgreso).HasMaxLength(Hospitalizacion.LargoMaximoObservacion);
        builder.Property(h => h.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(h => h.CamaId, IndiceCamaActiva).HasDatabaseName(IndiceCamaActiva).IsUnique().HasFilter(SoloActivas);
        builder.HasIndex(h => h.PacienteId, IndicePacienteActivo).HasDatabaseName(IndicePacienteActivo).IsUnique().HasFilter(SoloActivas);
        builder.HasIndex(h => h.PacienteId);
        builder.HasIndex(h => h.CamaId);
        builder.HasIndex(h => h.Estado);

        // Nada se borra: las claves foráneas restringen la eliminación.
        builder.HasOne<Paciente>().WithMany().HasForeignKey(h => h.PacienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Cama>().WithMany().HasForeignKey(h => h.CamaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(h => h.RegistradoPor).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(h => h.EgresoRegistradoPor).OnDelete(DeleteBehavior.Restrict);
    }
}
