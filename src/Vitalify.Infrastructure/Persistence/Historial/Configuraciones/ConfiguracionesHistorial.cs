using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Telemetria;

namespace Vitalify.Infrastructure.Persistence.Historial.Configuraciones;

/// <summary>
/// La clave primaria incluye la columna de tiempo: deduplica los reenvíos de MQTT (QoS 1) y, si el historial pasara a
/// TimescaleDB, la tabla podría ser una hypertable sin cambiar la clave.
/// </summary>
internal sealed class ConfiguracionLecturaSignos : IEntityTypeConfiguration<LecturaSignos>
{
    public const string ClavePrimaria = "pk_lectura_signos";

    public void Configure(EntityTypeBuilder<LecturaSignos> builder)
    {
        builder.ToTable("lectura_signos");
        builder.HasKey(l => new { l.CodigoDispositivo, l.MedidoEn });
        builder.Property(l => l.CodigoDispositivo).HasMaxLength(Dispositivo.LargoMaximoCodigo);
        builder.Property(l => l.Temperatura).HasPrecision(4, 1);
        builder.Property(l => l.Origen).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(l => new { l.HospitalizacionId, l.MedidoEn }).IsDescending(false, true);
    }
}

/// <summary>
/// Resumen de 5 minutos de las lecturas de un episodio cerrado (retención, fase 7). La clave
/// <c>(hospitalizacion_id, inicio)</c> incluye la columna de tiempo. Los promedios son <c>numeric</c> sin escala fija
/// para no perder precisión al reagrupar con el promedio ponderado.
/// </summary>
internal sealed class ConfiguracionLecturaSignosResumen : IEntityTypeConfiguration<LecturaSignosResumen>
{
    public void Configure(EntityTypeBuilder<LecturaSignosResumen> builder)
    {
        builder.ToTable("lectura_signos_resumen");
        builder.HasKey(r => new { r.HospitalizacionId, r.Inicio });
        foreach (var promedio in new[]
                 {
                     nameof(LecturaSignosResumen.FcPromedio), nameof(LecturaSignosResumen.FrPromedio), nameof(LecturaSignosResumen.Spo2Promedio),
                     nameof(LecturaSignosResumen.TemperaturaPromedio), nameof(LecturaSignosResumen.PasPromedio), nameof(LecturaSignosResumen.PadPromedio),
                 })
        {
            builder.Property(promedio).HasColumnType("numeric");
        }

        builder.Property(r => r.TemperaturaMinimo).HasPrecision(4, 1);
        builder.Property(r => r.TemperaturaMaximo).HasPrecision(4, 1);
    }
}

internal sealed class ConfiguracionEstadoSignosActual : IEntityTypeConfiguration<EstadoSignosActual>
{
    public const string ClavePrimaria = "pk_estado_signos_actual";

    public void Configure(EntityTypeBuilder<EstadoSignosActual> builder)
    {
        builder.ToTable("estado_signos_actual");
        builder.HasKey(e => e.HospitalizacionId);
        builder.Property(e => e.HospitalizacionId).ValueGeneratedNever();
        builder.Property(e => e.CodigoDispositivo).HasMaxLength(Dispositivo.LargoMaximoCodigo).IsRequired();
        builder.Property(e => e.TempValor).HasPrecision(4, 1);

        // Si dos lecturas del mismo paciente se procesan a la vez, la segunda recibe un conflicto y se reintenta:
        // así ninguna variable retrocede por una escritura concurrente.
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
    }
}

internal sealed class ConfiguracionIncidenciaTelemetria : IEntityTypeConfiguration<IncidenciaTelemetria>
{
    public void Configure(EntityTypeBuilder<IncidenciaTelemetria> builder)
    {
        builder.ToTable("incidencia_telemetria");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.CodigoDispositivo).HasMaxLength(IncidenciaTelemetria.LargoMaximoCodigo).IsRequired();
        builder.Property(i => i.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(i => i.Variable).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.ValorRecibido).HasPrecision(12, 3);
        builder.Property(i => i.Detalle).HasMaxLength(IncidenciaTelemetria.LargoMaximoDetalle).IsRequired();
        builder.HasIndex(i => i.OcurridaEn).IsDescending();
    }
}
