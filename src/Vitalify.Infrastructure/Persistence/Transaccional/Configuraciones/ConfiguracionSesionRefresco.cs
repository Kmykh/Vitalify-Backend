using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Sesiones;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionSesionRefresco : IEntityTypeConfiguration<SesionRefresco>
{
    public void Configure(EntityTypeBuilder<SesionRefresco> builder)
    {
        builder.ToTable("sesion_refresco");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        // SHA-256 en hexadecimal: 64 caracteres. El token en claro nunca se guarda.
        builder.Property(s => s.HashToken).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.HasIndex(s => s.HashToken).IsUnique();
        builder.HasIndex(s => s.UsuarioId);

        builder.HasOne<Usuario>().WithMany().HasForeignKey(s => s.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
