using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Pacientes;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionPaciente : IEntityTypeConfiguration<Paciente>
{
    public const string IndiceDocumento = "ux_paciente_documento";

    public void Configure(EntityTypeBuilder<Paciente> builder)
    {
        builder.ToTable("paciente");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.NombreCompleto).HasMaxLength(Paciente.LargoMaximoNombre).IsRequired();
        builder.Property(p => p.TipoDocumento).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.NumeroDocumento).HasMaxLength(Paciente.LargoMaximoDocumento).IsRequired();
        builder.HasIndex(p => new { p.TipoDocumento, p.NumeroDocumento }).IsUnique().HasDatabaseName(IndiceDocumento);
    }
}
