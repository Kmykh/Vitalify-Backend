using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Camas;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionCama : IEntityTypeConfiguration<Cama>
{
    public const string IndiceCodigo = "ux_cama_codigo";

    public void Configure(EntityTypeBuilder<Cama> builder)
    {
        builder.ToTable("cama");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Codigo).HasMaxLength(Cama.LargoMaximoCodigo).IsRequired();
        builder.Property(c => c.Servicio).HasMaxLength(Cama.LargoMaximoServicio).IsRequired();
        builder.HasIndex(c => c.Codigo).IsUnique().HasDatabaseName(IndiceCodigo);
    }
}
