using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Notificaciones;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Servicios;
using Vitalify.Domain.Turnos;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

internal sealed class ConfiguracionServicio : IEntityTypeConfiguration<Servicio>
{
    public const string IndiceCodigo = "ux_servicio_codigo";

    public void Configure(EntityTypeBuilder<Servicio> builder)
    {
        builder.ToTable("servicio");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Codigo).HasMaxLength(Servicio.LargoMaximoCodigo).IsRequired();
        builder.Property(s => s.Nombre).HasMaxLength(Servicio.LargoMaximoNombre).IsRequired();
        builder.HasIndex(s => s.Codigo).IsUnique().HasDatabaseName(IndiceCodigo);
    }
}

internal sealed class ConfiguracionProgramacionTurno : IEntityTypeConfiguration<ProgramacionTurno>
{
    public const string IndiceTurno = "ux_programacion_turno";

    public void Configure(EntityTypeBuilder<ProgramacionTurno> builder)
    {
        builder.ToTable("programacion_turno");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.TipoTurno).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Letra).HasMaxLength(ProgramacionTurno.LargoMaximoLetra).IsRequired();
        builder.Ignore(p => p.Turno);
        builder.HasIndex(p => new { p.ServicioId, p.Fecha, p.TipoTurno, p.UsuarioId }).IsUnique().HasDatabaseName(IndiceTurno);
        builder.HasIndex(p => new { p.UsuarioId, p.Fecha });

        builder.HasOne<Servicio>().WithMany().HasForeignKey(p => p.ServicioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(p => p.CargadoPor).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionAlertaClinica : IEntityTypeConfiguration<AlertaClinica>
{
    /// <summary>Una alerta abierta (pendiente o en atención) por hospitalización y tipo.</summary>
    public const string IndiceAbierta = "ux_alerta_abierta";

    public void Configure(EntityTypeBuilder<AlertaClinica> builder)
    {
        builder.ToTable("alerta_clinica");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Tipo).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(a => a.Prioridad).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(a => a.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.NivelRiesgo).HasMaxLength(20);
        builder.Property(a => a.NotaAtencion).HasMaxLength(AlertaClinica.LargoMaximoNota);
        builder.Property(a => a.Detalle)
            .HasConversion(ConversoresJson.Conversor<IReadOnlyDictionary<string, string>>(), ConversoresJson.ComparadorDiccionario())
            .HasColumnType("jsonb");
        builder.Property(a => a.NotificadaA)
            .HasConversion(ConversoresJson.ConversorLista<NotificacionAlerta>(), ConversoresJson.ComparadorLista<NotificacionAlerta>())
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasIndex(a => new { a.HospitalizacionId, a.Tipo }).IsUnique().HasDatabaseName(IndiceAbierta)
            .HasFilter("estado IN ('Pendiente', 'EnAtencion')");
        builder.HasIndex(a => new { a.ServicioId, a.Estado });
        builder.HasIndex(a => a.PacienteId);
        builder.HasIndex(a => a.CreadaEn);

        // Historial del episodio y espera tras atender (fase 7): la última atendida de un tipo.
        builder.HasIndex(a => new { a.HospitalizacionId, a.Tipo, a.AtendidaEn });

        // Nada clínico se borra en cascada.
        builder.HasOne<Hospitalizacion>().WithMany().HasForeignKey(a => a.HospitalizacionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Paciente>().WithMany().HasForeignKey(a => a.PacienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Servicio>().WithMany().HasForeignKey(a => a.ServicioId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(a => a.TomadaPor).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(a => a.AtendidaPor).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionDispositivoMovil : IEntityTypeConfiguration<DispositivoMovil>
{
    public const string IndiceToken = "ux_dispositivo_movil_token";

    public void Configure(EntityTypeBuilder<DispositivoMovil> builder)
    {
        builder.ToTable("dispositivo_movil");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(d => d.Token).HasMaxLength(DispositivoMovil.LargoMaximoToken).IsRequired();
        builder.Property(d => d.Plataforma).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.HasIndex(d => d.TokenHash).IsUnique().HasDatabaseName(IndiceToken);
        builder.HasIndex(d => d.UsuarioId);

        // El token de un usuario borrado no sirve de nada: este sí puede borrarse en cascada (no es un dato clínico).
        builder.HasOne<Usuario>().WithMany().HasForeignKey(d => d.UsuarioId).OnDelete(DeleteBehavior.Cascade);
    }
}
