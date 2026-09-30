using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Auditoria;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionRegistroAuditoria : IEntityTypeConfiguration<RegistroAuditoria>
{
    public void Configure(EntityTypeBuilder<RegistroAuditoria> builder)
    {
        builder.ToTable("auditoria");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Accion).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.Ip).HasMaxLength(RegistroAuditoria.LargoMaximoIp);
        builder.Property(r => r.Detalle).HasMaxLength(RegistroAuditoria.LargoMaximoDetalle);

        builder.HasIndex(r => r.Fecha);
        builder.HasIndex(r => r.UsuarioId);

        // La auditoría se conserva aunque el usuario se elimine.
        builder.HasOne<Usuario>().WithMany().HasForeignKey(r => r.UsuarioId).OnDelete(DeleteBehavior.SetNull);
    }
}
