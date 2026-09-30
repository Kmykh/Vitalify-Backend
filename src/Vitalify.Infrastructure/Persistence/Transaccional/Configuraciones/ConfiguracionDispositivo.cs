using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Dispositivos;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionDispositivo : IEntityTypeConfiguration<Dispositivo>
{
    public const string IndiceCodigo = "ux_dispositivo_codigo";

    public void Configure(EntityTypeBuilder<Dispositivo> builder)
    {
        builder.ToTable("dispositivo");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Codigo).HasMaxLength(Dispositivo.LargoMaximoCodigo).IsRequired();
        builder.Property(d => d.Descripcion).HasMaxLength(Dispositivo.LargoMaximoDescripcion);
        builder.Property(d => d.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(d => d.Codigo).IsUnique().HasDatabaseName(IndiceCodigo);
        builder.HasIndex(d => d.Estado);

        // Concurrencia optimista con la columna de sistema xmin: si dos usuarios cambian el estado del mismo
        // dispositivo a la vez (por ejemplo, mantenimiento y vinculación), el segundo recibe un conflicto.
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
    }
}
