using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Sesiones;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionTokenRevocado : IEntityTypeConfiguration<TokenRevocado>
{
    public void Configure(EntityTypeBuilder<TokenRevocado> builder)
    {
        builder.ToTable("token_revocado");
        builder.HasKey(t => t.Jti);
        builder.Property(t => t.Jti).HasMaxLength(TokenRevocado.LargoMaximoJti);

        // Para limpiar más adelante los que ya expiraron.
        builder.HasIndex(t => t.ExpiraEn);

        builder.HasOne<Usuario>().WithMany().HasForeignKey(t => t.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
