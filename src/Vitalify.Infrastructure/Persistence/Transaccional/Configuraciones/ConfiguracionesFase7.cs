using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Notas;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionNotaClinica : IEntityTypeConfiguration<NotaClinica>
{
    public void Configure(EntityTypeBuilder<NotaClinica> builder)
    {
        builder.ToTable("nota_clinica");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.Texto).HasMaxLength(NotaClinica.LargoMaximoTexto).IsRequired();

        // Dos correcciones a la vez de la misma nota: la segunda recibe conflicto-concurrencia (columna de sistema xmin).
        builder.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");

        builder.HasIndex(n => new { n.HospitalizacionId, n.CreadaEn }).IsDescending(false, true);
        builder.HasIndex(n => n.PacienteId);

        // Nada clínico se borra en cascada.
        builder.HasOne<Hospitalizacion>().WithMany().HasForeignKey(n => n.HospitalizacionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Paciente>().WithMany().HasForeignKey(n => n.PacienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(n => n.AutorId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionNotaClinicaVersion : IEntityTypeConfiguration<NotaClinicaVersion>
{
    /// <summary>Un número de versión por nota.</summary>
    public const string IndiceNumero = "ux_nota_clinica_version";

    public void Configure(EntityTypeBuilder<NotaClinicaVersion> builder)
    {
        builder.ToTable("nota_clinica_version");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(v => v.Texto).HasMaxLength(NotaClinica.LargoMaximoTexto).IsRequired();

        builder.HasIndex(v => new { v.NotaId, v.Numero }).IsUnique().HasDatabaseName(IndiceNumero);

        builder.HasOne<NotaClinica>().WithMany().HasForeignKey(v => v.NotaId).OnDelete(DeleteBehavior.Restrict);
    }
}
