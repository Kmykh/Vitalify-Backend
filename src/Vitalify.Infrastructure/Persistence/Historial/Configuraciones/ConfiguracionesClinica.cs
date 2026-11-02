using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Clinica;

namespace Vitalify.Infrastructure.Persistence.Historial.Configuraciones;

internal sealed class ConfiguracionObservacionEnfermeria : IEntityTypeConfiguration<ObservacionEnfermeria>
{
    public void Configure(EntityTypeBuilder<ObservacionEnfermeria> builder)
    {
        builder.ToTable("observacion_enfermeria");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Temperatura).HasPrecision(4, 1);
        builder.Property(o => o.Conciencia).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(o => new { o.HospitalizacionId, o.ObservadaEn }).IsDescending(false, true);
    }
}

internal sealed class ConfiguracionEvaluacionRiesgo : IEntityTypeConfiguration<EvaluacionRiesgo>
{
    public void Configure(EntityTypeBuilder<EvaluacionRiesgo> builder)
    {
        builder.ToTable("evaluacion_riesgo");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Origen).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Temperatura).HasPrecision(4, 1);
        builder.Property(e => e.Conciencia).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.EscalaSpo2).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(e => e.News2Nivel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.MewsNivel).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Ignore(e => e.Parametros);
        builder.HasIndex(e => new { e.HospitalizacionId, e.EvaluadaEn }).IsDescending(false, true);
    }
}
