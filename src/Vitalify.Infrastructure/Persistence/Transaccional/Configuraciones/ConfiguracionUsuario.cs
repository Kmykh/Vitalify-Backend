using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionUsuario : IEntityTypeConfiguration<Usuario>
{
    /// <summary>Nombre del índice único; <see cref="UnidadDeTrabajo"/> lo usa para traducir la violación a Conflicto.</summary>
    public const string IndiceCorreo = "ux_usuario_correo";

    public void Configure(EntityTypeBuilder<Usuario> builder)
    {
        builder.ToTable("usuario");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Nombre).HasMaxLength(Usuario.LargoMaximoNombre).IsRequired();
        builder.Property(u => u.Correo)
            .HasConversion(c => c.Valor, v => Correo.Crear(v))
            .HasMaxLength(Correo.LargoMaximo)
            .IsRequired();
        builder.HasIndex(u => u.Correo).IsUnique().HasDatabaseName(IndiceCorreo);

        builder.Property(u => u.HashContrasena).HasMaxLength(512).IsRequired();
        builder.Property(u => u.Rol).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(u => u.Rol);
    }
}
