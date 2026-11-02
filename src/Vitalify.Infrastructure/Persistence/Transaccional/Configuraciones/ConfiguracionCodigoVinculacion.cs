using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Dispositivos;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionCodigoVinculacion : IEntityTypeConfiguration<CodigoVinculacion>
{
    /// <summary>Un dispositivo tiene como máximo un código activo (ni usado ni invalidado).</summary>
    public const string IndiceActivo = "ux_codigo_vinculacion_activo";

    public const string IndiceToken = "ux_codigo_vinculacion_token";

    public void Configure(EntityTypeBuilder<CodigoVinculacion> builder)
    {
        builder.ToTable("codigo_vinculacion");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // Solo hashes SHA-256 (64 caracteres hexadecimales): ni el código ni el token se guardan en claro.
        builder.Property(c => c.HashCodigo).HasMaxLength(CodigoVinculacion.LargoHash).IsFixedLength().IsRequired();
        builder.Property(c => c.TokenHash).HasMaxLength(CodigoVinculacion.LargoHash).IsFixedLength();

        builder.HasIndex(c => c.DispositivoId, IndiceActivo).HasDatabaseName(IndiceActivo).IsUnique()
            .HasFilter("usado_en IS NULL AND invalidado_en IS NULL");
        builder.HasIndex(c => c.TokenHash, IndiceToken).HasDatabaseName(IndiceToken).IsUnique()
            .HasFilter("token_hash IS NOT NULL");
        builder.HasIndex(c => c.DispositivoId);

        builder.HasOne<Dispositivo>().WithMany().HasForeignKey(c => c.DispositivoId).OnDelete(DeleteBehavior.Restrict);

        // Dos escaneos simultáneos del mismo código: el segundo recibe un conflicto en vez de pisar los intentos o el token.
        builder.Property<uint>("Version").IsRowVersion().HasColumnName("xmin");
    }
}
