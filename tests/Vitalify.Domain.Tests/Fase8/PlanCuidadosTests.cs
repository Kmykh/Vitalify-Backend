using Vitalify.Domain.Comun;
using Vitalify.Domain.Nnn;

namespace Vitalify.Domain.Tests.Fase8;

/// <summary>Reglas del plan de cuidados: nada se borra, un diagnóstico no vuelve a Activo y las puntuaciones NOC van de 1 a 5.</summary>
public sealed class PlanCuidadosTests
{
    private static readonly DateTime Ahora = new(2026, 11, 9, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Enfermera = Guid.NewGuid();

    private static DiagnosticoPlan Activo() =>
        DiagnosticoPlan.Confirmar(Guid.NewGuid(), Guid.NewGuid(), "00032", OrigenDiagnostico.Sugerido, "R1", null, Guid.NewGuid(),
            MotorSugerenciasNnn.VersionReglas, Enfermera, Ahora);

    [Fact]
    public void Confirmar_deja_el_diagnostico_activo_con_quien_y_cuando()
    {
        var d = Activo();

        Assert.Equal(EstadoDiagnosticoPlan.Activo, d.Estado);
        Assert.Equal((Enfermera, Ahora), (d.ConfirmadoPor!.Value, d.ConfirmadoEn!.Value));
        Assert.Equal("R1", d.ReglaOrigen);
        Assert.Equal("2026.11.1", d.VersionReglas);
    }

    [Fact]
    public void Un_diagnostico_sugerido_exige_la_regla_y_uno_manual_no_la_guarda()
    {
        Assert.Throws<ExcepcionDeDominio>(() => DiagnosticoPlan.Confirmar(
            Guid.NewGuid(), Guid.NewGuid(), "00032", OrigenDiagnostico.Sugerido, null, null, null, null, Enfermera, Ahora));

        var manual = DiagnosticoPlan.Confirmar(Guid.NewGuid(), Guid.NewGuid(), "00146", OrigenDiagnostico.Manual, "R1", null, null, null, Enfermera, Ahora);
        Assert.Null(manual.ReglaOrigen);
    }

    [Theory]
    [InlineData("0032")]
    [InlineData("000321")]
    [InlineData("0003a")]
    public void El_codigo_NANDA_tiene_5_digitos(string codigo) =>
        Assert.Throws<ExcepcionDeDominio>(() => DiagnosticoPlan.Confirmar(
            Guid.NewGuid(), Guid.NewGuid(), codigo, OrigenDiagnostico.Manual, null, null, null, null, Enfermera, Ahora));

    [Theory]
    [InlineData(null)]
    [InlineData("no")]
    public void Descartar_una_sugerencia_exige_un_motivo_de_5_a_300_caracteres(string? motivo) =>
        Assert.Throws<ExcepcionDeDominio>(() => DiagnosticoPlan.DescartarSugerencia(
            Guid.NewGuid(), Guid.NewGuid(), "00032", "R1", null, null, "2026.11.1", motivo!, Enfermera, Ahora));

    [Fact]
    public void Descartar_una_sugerencia_queda_registrado_sin_haber_estado_activa()
    {
        var d = DiagnosticoPlan.DescartarSugerencia(Guid.NewGuid(), Guid.NewGuid(), "00032", "R1", null, null, "2026.11.1",
            "  FR por llanto, ya normalizó  ", Enfermera, Ahora);

        Assert.Equal(EstadoDiagnosticoPlan.Descartado, d.Estado);
        Assert.Equal("FR por llanto, ya normalizó", d.MotivoCierre);
        Assert.Null(d.ConfirmadoPor);
        Assert.Equal(Enfermera, d.CerradoPor);
    }

    [Fact]
    public void Un_diagnostico_cerrado_no_vuelve_a_activo_ni_se_cierra_otra_vez()
    {
        var d = Activo();
        d.Cerrar(EstadoDiagnosticoPlan.Resuelto, "Patrón normalizado", Enfermera, Ahora.AddHours(4));

        Assert.Equal(EstadoDiagnosticoPlan.Resuelto, d.Estado);
        Assert.Throws<ExcepcionDeDominio>(() => d.Cerrar(EstadoDiagnosticoPlan.Activo, "Reaparece", Enfermera, Ahora));
        Assert.Throws<ExcepcionDeDominio>(() => d.Cerrar(EstadoDiagnosticoPlan.Descartado, "Otra vez", Enfermera, Ahora));
    }

    [Fact]
    public void El_egreso_resuelve_con_el_motivo_Egreso()
    {
        var d = Activo();
        d.ResolverPorEgreso(Enfermera, Ahora);

        Assert.Equal((EstadoDiagnosticoPlan.Resuelto, "Egreso"), (d.Estado, d.MotivoCierre));
    }

    [Fact]
    public void Una_intervencion_de_una_sola_vez_deja_de_estar_activa_al_ejecutarse()
    {
        var unaVez = IntervencionPlan.Planificar(Guid.NewGuid(), "7710", FrecuenciaIntervencion.UnaVez, Enfermera, Ahora);
        var cadaHora = IntervencionPlan.Planificar(Guid.NewGuid(), "3350", FrecuenciaIntervencion.CadaHora, Enfermera, Ahora);

        var e = unaVez.Ejecutar(Enfermera, Ahora, Guid.NewGuid(), "  Médico avisado  ");
        cadaHora.Ejecutar(Enfermera, Ahora, null, null);
        cadaHora.Ejecutar(Enfermera, Ahora.AddHours(1), null, " ");

        Assert.False(unaVez.Activa);
        Assert.True(cadaHora.Activa);
        Assert.Equal("Médico avisado", e.Observacion);
        Assert.Throws<ExcepcionDeDominio>(() => unaVez.Ejecutar(Enfermera, Ahora, null, null));
    }

    [Fact]
    public void La_observacion_de_una_ejecucion_tiene_como_maximo_500_caracteres()
    {
        var i = IntervencionPlan.Planificar(Guid.NewGuid(), "3350", FrecuenciaIntervencion.CadaTurno, Enfermera, Ahora);

        Assert.Throws<ExcepcionDeDominio>(() => i.Ejecutar(Enfermera, Ahora, null, new string('x', 501)));
    }

    [Theory]
    [InlineData(2, 4, true)]
    [InlineData(3, 3, true)]
    [InlineData(4, 2, false)]
    [InlineData(0, 3, false)]
    [InlineData(2, 6, false)]
    public void La_meta_NOC_es_mayor_o_igual_que_la_basal_y_ambas_van_de_1_a_5(int basal, int meta, bool valida)
    {
        if (valida)
        {
            Assert.Equal(meta, ResultadoPlan.Planificar(Guid.NewGuid(), "0415", basal, meta).PuntuacionMeta);
        }
        else
        {
            Assert.Throws<ExcepcionDeDominio>(() => ResultadoPlan.Planificar(Guid.NewGuid(), "0415", basal, meta));
        }
    }

    [Fact]
    public void Una_valoracion_va_de_1_a_5_y_el_indicador_tiene_6_digitos_o_es_global()
    {
        Assert.Null(ValoracionResultado.Registrar(Guid.NewGuid(), null, 3, null, Enfermera, Ahora, "2026-11-09/GuardiaDiurna").IndicadorCodigo);
        Assert.Throws<ExcepcionDeDominio>(() => ValoracionResultado.Registrar(Guid.NewGuid(), "0802", 3, null, Enfermera, Ahora, null));
        Assert.Throws<ExcepcionDeDominio>(() => ValoracionResultado.Registrar(Guid.NewGuid(), "080204", 6, null, Enfermera, Ahora, null));
        Assert.Throws<ExcepcionDeDominio>(() => ValoracionResultado.Registrar(Guid.NewGuid(), "080204", 3, 0, Enfermera, Ahora, null));
    }

    [Fact]
    public void Una_sugerencia_emitida_se_resuelve_una_sola_vez()
    {
        var sugerencia = new SugerenciaNnn("00032", ["3350"], ["0415"], ["R1"], "FR 22 rpm", PrioridadSugerencia.Media, null, null);
        var s = SugerenciaEmitida.Emitir(Guid.NewGuid(), Guid.NewGuid(), sugerencia, "2026.11.1", Ahora);

        s.Confirmar(Guid.NewGuid(), Ahora.AddMinutes(3));

        Assert.Equal(EstadoSugerencia.Confirmada, s.Estado);
        Assert.Throws<ExcepcionDeDominio>(() => s.Expirar(Ahora));
    }

    [Fact]
    public void Las_entradas_del_catalogo_validan_su_codigo_y_la_verificacion_es_idempotente()
    {
        Assert.Throws<ExcepcionDeDominio>(() => IntervencionNic.Crear("33500", "Monitorización respiratoria", null, "NIC 8a ed."));
        Assert.Throws<ExcepcionDeDominio>(() => IndicadorNoc.Crear("041501", "0802", "Frecuencia respiratoria", ParametroVital.Fr, "NOC 7a ed."));

        var d = DiagnosticoNanda.Crear("00032", "Patrón respiratorio ineficaz", "Actividad/reposo", null, TipoDiagnosticoNanda.Real, "NANDA-I 2024-2026");
        var admin = Guid.NewGuid();
        d.Verificar(admin, Ahora);
        d.Verificar(Guid.NewGuid(), Ahora.AddDays(1));

        Assert.True(d.Verificado);
        Assert.Equal((admin, Ahora), (d.VerificadoPor!.Value, d.VerificadoEn!.Value));
        Assert.False(d.Actualizar("Patrón respiratorio ineficaz", "Actividad/reposo", null, TipoDiagnosticoNanda.Real, "NANDA-I 2024-2026"));
    }
}
