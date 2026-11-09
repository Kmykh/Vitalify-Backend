using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Vitalify.Domain.Alertas;
using Vitalify.Domain.Hospitalizaciones;
using Vitalify.Domain.Nnn;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Usuarios;

namespace Vitalify.Infrastructure.Persistence.Transaccional.Configuraciones;

/// <summary>Columnas comunes de las entradas del catálogo NNN (código, etiqueta, edición, verificación y si está activa).</summary>
internal static class ColumnasCatalogoNnn
{
    public static void Configurar<T>(EntityTypeBuilder<T> builder, string tabla, int largoCodigo)
        where T : EntradaCatalogoNnn
    {
        builder.ToTable(tabla);
        builder.HasKey(e => e.Codigo);
        builder.Property(e => e.Codigo).HasMaxLength(largoCodigo).ValueGeneratedNever();
        builder.Property(e => e.Etiqueta).HasMaxLength(EntradaCatalogoNnn.LargoMaximoEtiqueta).IsRequired();
        builder.Property(e => e.Edicion).HasMaxLength(EntradaCatalogoNnn.LargoMaximoEdicion).IsRequired();
        builder.Ignore(e => e.Taxonomia);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(e => e.VerificadoPor).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionDiagnosticoNanda : IEntityTypeConfiguration<DiagnosticoNanda>
{
    public void Configure(EntityTypeBuilder<DiagnosticoNanda> builder)
    {
        ColumnasCatalogoNnn.Configurar(builder, "nnn_diagnostico", CodigosNnn.LargoDiagnostico);
        builder.Property(d => d.Dominio).HasMaxLength(DiagnosticoNanda.LargoMaximoDominio);
        builder.Property(d => d.Clase).HasMaxLength(DiagnosticoNanda.LargoMaximoDominio);
        builder.Property(d => d.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
    }
}

internal sealed class ConfiguracionIntervencionNic : IEntityTypeConfiguration<IntervencionNic>
{
    public void Configure(EntityTypeBuilder<IntervencionNic> builder)
    {
        ColumnasCatalogoNnn.Configurar(builder, "nnn_intervencion", CodigosNnn.LargoIntervencion);
        builder.Property(i => i.Campo).HasMaxLength(IntervencionNic.LargoMaximoCampo);
    }
}

internal sealed class ConfiguracionResultadoNoc : IEntityTypeConfiguration<ResultadoNoc>
{
    public void Configure(EntityTypeBuilder<ResultadoNoc> builder)
    {
        ColumnasCatalogoNnn.Configurar(builder, "nnn_resultado", CodigosNnn.LargoResultado);
        builder.Property(r => r.Escala).HasMaxLength(ResultadoNoc.LargoMaximoEscala).IsRequired();
    }
}

internal sealed class ConfiguracionIndicadorNoc : IEntityTypeConfiguration<IndicadorNoc>
{
    public void Configure(EntityTypeBuilder<IndicadorNoc> builder)
    {
        ColumnasCatalogoNnn.Configurar(builder, "nnn_indicador", CodigosNnn.LargoIndicador);
        builder.Property(i => i.ResultadoCodigo).HasMaxLength(CodigosNnn.LargoResultado).IsRequired();
        builder.Property(i => i.ParametroVital).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(i => i.ResultadoCodigo);
        builder.HasOne<ResultadoNoc>().WithMany().HasForeignKey(i => i.ResultadoCodigo).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionVinculoNnn : IEntityTypeConfiguration<VinculoNnn>
{
    public void Configure(EntityTypeBuilder<VinculoNnn> builder)
    {
        builder.ToTable("nnn_vinculo");
        builder.HasKey(v => new { v.DiagnosticoCodigo, v.Tipo, v.Codigo });
        builder.Property(v => v.DiagnosticoCodigo).HasMaxLength(CodigosNnn.LargoDiagnostico);
        builder.Property(v => v.Tipo).HasConversion<string>().HasMaxLength(10);
        builder.Property(v => v.Codigo).HasMaxLength(CodigosNnn.LargoResultado);
        builder.HasOne<DiagnosticoNanda>().WithMany().HasForeignKey(v => v.DiagnosticoCodigo).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionDiagnosticoPlan : IEntityTypeConfiguration<DiagnosticoPlan>
{
    /// <summary>Un mismo diagnóstico NANDA-I no puede estar activo dos veces en la misma hospitalización.</summary>
    public const string IndiceActivo = "ux_plan_diagnostico_activo";

    public void Configure(EntityTypeBuilder<DiagnosticoPlan> builder)
    {
        builder.ToTable("plan_diagnostico");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DiagnosticoCodigo).HasMaxLength(CodigosNnn.LargoDiagnostico).IsRequired();
        builder.Property(d => d.Origen).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.ReglaOrigen).HasMaxLength(DiagnosticoPlan.LargoMaximoRegla);
        builder.Property(d => d.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.MotivoCierre).HasMaxLength(DiagnosticoPlan.LargoMaximoMotivo);
        builder.Property(d => d.VersionReglas).HasMaxLength(DiagnosticoPlan.LargoMaximoVersion);

        builder.HasIndex(d => new { d.HospitalizacionId, d.DiagnosticoCodigo })
            .IsUnique().HasFilter("estado = 'Activo'").HasDatabaseName(IndiceActivo);
        builder.HasIndex(d => new { d.HospitalizacionId, d.Estado });
        builder.HasIndex(d => d.ConfirmadoEn);
        builder.HasIndex(d => d.PacienteId);

        // Nada clínico se borra en cascada.
        builder.HasOne<Hospitalizacion>().WithMany().HasForeignKey(d => d.HospitalizacionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Paciente>().WithMany().HasForeignKey(d => d.PacienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DiagnosticoNanda>().WithMany().HasForeignKey(d => d.DiagnosticoCodigo).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlertaClinica>().WithMany().HasForeignKey(d => d.AlertaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(d => d.ConfirmadoPor).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(d => d.CerradoPor).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionIntervencionPlan : IEntityTypeConfiguration<IntervencionPlan>
{
    /// <summary>Una intervención NIC activa por diagnóstico.</summary>
    public const string IndiceActiva = "ux_plan_intervencion_activa";

    public void Configure(EntityTypeBuilder<IntervencionPlan> builder)
    {
        builder.ToTable("plan_intervencion");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.IntervencionCodigo).HasMaxLength(CodigosNnn.LargoIntervencion).IsRequired();
        builder.Property(i => i.Frecuencia).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(i => new { i.PlanDiagnosticoId, i.IntervencionCodigo })
            .IsUnique().HasFilter("activa").HasDatabaseName(IndiceActiva);

        builder.HasOne<DiagnosticoPlan>().WithMany().HasForeignKey(i => i.PlanDiagnosticoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IntervencionNic>().WithMany().HasForeignKey(i => i.IntervencionCodigo).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(i => i.AgregadaPor).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionEjecucionIntervencion : IEntityTypeConfiguration<EjecucionIntervencion>
{
    public void Configure(EntityTypeBuilder<EjecucionIntervencion> builder)
    {
        builder.ToTable("plan_intervencion_ejecucion");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Observacion).HasMaxLength(EjecucionIntervencion.LargoMaximoObservacion);

        builder.HasIndex(e => new { e.PlanIntervencionId, e.EjecutadaEn });
        builder.HasIndex(e => e.AlertaId);

        builder.HasOne<IntervencionPlan>().WithMany().HasForeignKey(e => e.PlanIntervencionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlertaClinica>().WithMany().HasForeignKey(e => e.AlertaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(e => e.EjecutadaPor).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionResultadoPlan : IEntityTypeConfiguration<ResultadoPlan>
{
    public const string IndiceResultado = "ux_plan_resultado";

    public void Configure(EntityTypeBuilder<ResultadoPlan> builder)
    {
        builder.ToTable("plan_resultado", t =>
        {
            t.HasCheckConstraint("ck_plan_resultado_puntuaciones",
                "puntuacion_basal BETWEEN 1 AND 5 AND puntuacion_meta BETWEEN 1 AND 5 AND puntuacion_meta >= puntuacion_basal");
        });
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ResultadoCodigo).HasMaxLength(CodigosNnn.LargoResultado).IsRequired();

        builder.HasIndex(r => new { r.PlanDiagnosticoId, r.ResultadoCodigo }).IsUnique().HasDatabaseName(IndiceResultado);

        builder.HasOne<DiagnosticoPlan>().WithMany().HasForeignKey(r => r.PlanDiagnosticoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ResultadoNoc>().WithMany().HasForeignKey(r => r.ResultadoCodigo).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionValoracionResultado : IEntityTypeConfiguration<ValoracionResultado>
{
    public void Configure(EntityTypeBuilder<ValoracionResultado> builder)
    {
        builder.ToTable("plan_resultado_valoracion", t =>
        {
            t.HasCheckConstraint("ck_plan_resultado_valoracion_puntuacion",
                "puntuacion BETWEEN 1 AND 5 AND (puntuacion_sugerida IS NULL OR puntuacion_sugerida BETWEEN 1 AND 5)");
        });
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.IndicadorCodigo).HasMaxLength(CodigosNnn.LargoIndicador);
        builder.Property(v => v.Turno).HasMaxLength(ValoracionResultado.LargoMaximoTurno);

        builder.HasIndex(v => new { v.PlanResultadoId, v.ValoradaEn });

        builder.HasOne<ResultadoPlan>().WithMany().HasForeignKey(v => v.PlanResultadoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IndicadorNoc>().WithMany().HasForeignKey(v => v.IndicadorCodigo).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Usuario>().WithMany().HasForeignKey(v => v.ValoradaPor).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ConfiguracionSugerenciaEmitida : IEntityTypeConfiguration<SugerenciaEmitida>
{
    /// <summary>Como máximo una sugerencia pendiente por diagnóstico y hospitalización.</summary>
    public const string IndicePendiente = "ux_plan_sugerencia_pendiente";

    public void Configure(EntityTypeBuilder<SugerenciaEmitida> builder)
    {
        builder.ToTable("plan_sugerencia");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DiagnosticoCodigo).HasMaxLength(CodigosNnn.LargoDiagnostico).IsRequired();
        builder.Property(s => s.Regla).HasMaxLength(DiagnosticoPlan.LargoMaximoRegla).IsRequired();
        builder.Property(s => s.VersionReglas).HasMaxLength(DiagnosticoPlan.LargoMaximoVersion).IsRequired();
        builder.Property(s => s.Prioridad).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(s => s.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(s => new { s.HospitalizacionId, s.DiagnosticoCodigo })
            .IsUnique().HasFilter("estado = 'Pendiente'").HasDatabaseName(IndicePendiente);
        builder.HasIndex(s => s.EmitidaEn);

        builder.HasOne<Hospitalizacion>().WithMany().HasForeignKey(s => s.HospitalizacionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Paciente>().WithMany().HasForeignKey(s => s.PacienteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DiagnosticoNanda>().WithMany().HasForeignKey(s => s.DiagnosticoCodigo).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlertaClinica>().WithMany().HasForeignKey(s => s.AlertaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DiagnosticoPlan>().WithMany().HasForeignKey(s => s.PlanDiagnosticoId).OnDelete(DeleteBehavior.Restrict);
    }
}
