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

        // Asistente de toma manual (fase 5). Todo es opcional: las tomas de la fase 4 no lo tienen.
        builder.Property(o => o.DispositivoOxigeno).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.FlujoOxigenoLpm).HasPrecision(4, 1);
        builder.Property(o => o.ObjetivoSpo288a92).HasColumnName("objetivo_spo2_88a92");
        builder.Property(o => o.PosicionPa).HasColumnName("posicion_pa").HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.BrazoPa).HasColumnName("brazo_pa").HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.MetodoPa).HasColumnName("metodo_pa").HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.ChecklistPa).HasColumnName("checklist_pa")
            .HasConversion(ConversoresJson.Conversor<ChecklistPresion>()).HasColumnType("jsonb");
        builder.Property(o => o.LecturasPa).HasColumnName("lecturas_pa")
            .HasConversion(ConversoresJson.Conversor<IReadOnlyList<LecturaPresion>>(), ConversoresJson.ComparadorLista<LecturaPresion>())
            .HasColumnType("jsonb");
        builder.Property(o => o.Contexto).HasConversion(ConversoresJson.Conversor<ContextoClinico>()).HasColumnType("jsonb");
        builder.Property(o => o.VersionGuia).HasMaxLength(ObservacionEnfermeria.LargoMaximoVersionGuia);

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
