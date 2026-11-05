using Vitalify.Domain.Alertas;
using Vitalify.Domain.Comun;
using Vitalify.Domain.Historial;
using Vitalify.Domain.Notas;

namespace Vitalify.Domain.Tests.Fase7;

public class NotaClinicaTests
{
    private static readonly DateTime Ahora = new(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Autor = Guid.NewGuid();

    private static NotaClinica Nota(string texto = "Paciente estable, tolera vía oral.") =>
        NotaClinica.Registrar(Guid.NewGuid(), Guid.NewGuid(), Autor, TipoNota.Evolucion, texto, Ahora);

    [Fact]
    public void Al_registrarla_queda_en_la_version_1_sin_edicion_y_con_el_texto_limpio()
    {
        var nota = Nota("  Paciente estable.  ");

        Assert.Equal(("Paciente estable.", 1, (DateTime?)null, Autor), (nota.Texto, nota.Version, nota.EditadaEn, nota.AutorId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("abcd")]
    public void Un_texto_de_menos_de_5_caracteres_no_es_valido(string texto) =>
        Assert.Throws<ExcepcionDeDominio>(() => Nota(texto));

    [Fact]
    public void Un_texto_de_mas_de_4000_caracteres_no_es_valido()
    {
        Assert.Throws<ExcepcionDeDominio>(() => Nota(new string('a', NotaClinica.LargoMaximoTexto + 1)));
        Assert.Equal(NotaClinica.LargoMaximoTexto, Nota(new string('a', NotaClinica.LargoMaximoTexto)).Texto.Length);
    }

    [Fact]
    public void Un_tipo_inexistente_no_es_valido() =>
        Assert.Throws<ExcepcionDeDominio>(() => NotaClinica.Registrar(Guid.NewGuid(), Guid.NewGuid(), Autor, (TipoNota)99, "Texto válido", Ahora));

    [Fact]
    public void Al_editarla_guarda_la_version_anterior_con_su_vigencia_y_sube_el_numero()
    {
        var nota = Nota("Primer texto de la nota.");

        var v1 = nota.Editar(Autor, TipoNota.Evolucion, "Segundo texto de la nota.", Ahora.AddHours(1))!;
        var v2 = nota.Editar(Autor, TipoNota.Interconsulta, "Tercer texto de la nota.", Ahora.AddHours(2))!;

        Assert.Equal((1, "Primer texto de la nota.", Ahora, Ahora.AddHours(1)), (v1.Numero, v1.Texto, v1.VigenteDesde, v1.ReemplazadaEn));
        Assert.Equal((2, TipoNota.Evolucion, Ahora.AddHours(1), Ahora.AddHours(2)), (v2.Numero, v2.Tipo, v2.VigenteDesde, v2.ReemplazadaEn));
        Assert.Equal((3, TipoNota.Interconsulta, "Tercer texto de la nota.", (DateTime?)Ahora.AddHours(2)),
            (nota.Version, nota.Tipo, nota.Texto, nota.EditadaEn));
        Assert.All([v1, v2], v => Assert.Equal(nota.Id, v.NotaId));
    }

    [Fact]
    public void Editarla_con_el_mismo_texto_y_tipo_no_crea_version()
    {
        var nota = Nota();

        Assert.Null(nota.Editar(Autor, TipoNota.Evolucion, "  Paciente estable, tolera vía oral. ", Ahora.AddHours(1)));
        Assert.Equal((1, (DateTime?)null), (nota.Version, nota.EditadaEn));
    }

    [Fact]
    public void Solo_el_autor_la_edita()
    {
        var nota = Nota();
        var otro = Guid.NewGuid();

        Assert.Equal(MotivoNoEditable.OtroAutor, nota.PorQueNoEditable(otro, Ahora));
        Assert.Throws<ExcepcionDeDominio>(() => nota.Editar(otro, TipoNota.Evolucion, "Texto de otra persona", Ahora));
    }

    [Fact]
    public void Se_edita_hasta_las_24_horas_exactas_y_no_despues()
    {
        var nota = Nota();

        Assert.Null(nota.PorQueNoEditable(Autor, Ahora.AddHours(24)));
        Assert.Equal(MotivoNoEditable.FueraDePlazo, nota.PorQueNoEditable(Autor, Ahora.AddHours(24).AddSeconds(1)));
        Assert.Throws<ExcepcionDeDominio>(() => nota.Editar(Autor, TipoNota.Evolucion, "Corrección tardía", Ahora.AddHours(25)));
    }

    [Fact]
    public void El_plazo_se_cuenta_desde_la_creacion_y_no_desde_la_ultima_edicion()
    {
        var nota = Nota();
        nota.Editar(Autor, TipoNota.Evolucion, "Corrección a las 20 horas.", Ahora.AddHours(20));

        Assert.Equal(MotivoNoEditable.FueraDePlazo, nota.PorQueNoEditable(Autor, Ahora.AddHours(25)));
    }
}

public class VentanaHistorialTests
{
    private static readonly DateTime Ingreso = new(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ahora = Ingreso.AddDays(2);

    [Fact]
    public void Por_defecto_es_el_episodio_hasta_ahora_si_sigue_hospitalizado()
    {
        var ventana = VentanaHistorial.Resolver(Ingreso, null, Ahora, null, null, agrupado: false);

        Assert.Equal((true, Ingreso, Ahora), (ventana.EsValida, ventana.Desde, ventana.Hasta));
    }

    [Fact]
    public void Por_defecto_termina_en_el_egreso_si_ya_egreso()
    {
        var egreso = Ingreso.AddDays(1);

        var ventana = VentanaHistorial.Resolver(Ingreso, egreso, Ahora, null, null, agrupado: false);

        Assert.Equal((Ingreso, egreso), (ventana.Desde, ventana.Hasta));
    }

    [Fact]
    public void Un_rango_invertido_es_un_error_en_hasta()
    {
        var ventana = VentanaHistorial.Resolver(Ingreso, null, Ahora, Ahora, Ahora.AddHours(-1), agrupado: false);

        Assert.Equal((false, "hasta"), (ventana.EsValida, ventana.CampoInvalido));
    }

    [Fact]
    public void Sin_agrupar_se_permiten_7_dias_exactos_y_no_uno_mas()
    {
        Assert.True(VentanaHistorial.Resolver(Ingreso, null, Ahora, Ahora.AddDays(-7), Ahora, agrupado: false).EsValida);

        var ventana = VentanaHistorial.Resolver(Ingreso, null, Ahora, Ahora.AddDays(-7).AddSeconds(-1), Ahora, agrupado: false);

        Assert.Equal((false, "desde"), (ventana.EsValida, ventana.CampoInvalido));
    }

    [Fact]
    public void Un_episodio_de_mas_de_7_dias_sin_rango_ni_agrupar_pide_agrupar()
    {
        var ventana = VentanaHistorial.Resolver(Ingreso, null, Ingreso.AddDays(10), null, null, agrupado: false);

        Assert.Equal((false, "agrupar"), (ventana.EsValida, ventana.CampoInvalido));
    }

    [Fact]
    public void Agrupado_devuelve_el_episodio_completo_aunque_dure_mas_de_7_dias()
    {
        var ahora = Ingreso.AddDays(30);

        var ventana = VentanaHistorial.Resolver(Ingreso, null, ahora, null, null, agrupado: true);

        Assert.Equal((true, Ingreso, ahora), (ventana.EsValida, ventana.Desde, ventana.Hasta));
    }

    [Fact]
    public void Agrupado_no_se_puede_pedir_mas_que_el_episodio_si_este_dura_mas_de_7_dias()
    {
        var ahora = Ingreso.AddDays(30);

        var ventana = VentanaHistorial.Resolver(Ingreso, null, ahora, Ingreso.AddDays(-1), ahora, agrupado: true);

        Assert.Equal((false, "desde"), (ventana.EsValida, ventana.CampoInvalido));
    }
}

public class IntervaloAgrupacionTests
{
    [Theory]
    [InlineData("1m", 60)]
    [InlineData("5M", 300)]
    [InlineData(" 15m ", 900)]
    [InlineData("1h", 3600)]
    public void Reconoce_los_intervalos_de_signos(string codigo, int segundos) =>
        Assert.Equal(segundos, IntervaloAgrupacion.Buscar(codigo, IntervaloAgrupacion.ParaSignos)!.Segundos);

    [Theory]
    [InlineData("1m")]
    [InlineData("2h")]
    [InlineData("")]
    [InlineData(null)]
    public void El_riesgo_no_admite_1_minuto_ni_intervalos_desconocidos(string? codigo) =>
        Assert.Null(IntervaloAgrupacion.Buscar(codigo, IntervaloAgrupacion.ParaRiesgo));

    [Fact]
    public void El_inicio_del_grupo_se_alinea_al_intervalo()
    {
        var instante = new DateTime(2026, 11, 3, 14, 7, 42, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 11, 3, 14, 5, 0, DateTimeKind.Utc), IntervaloAgrupacion.CincoMinutos.InicioDe(instante));
        Assert.Equal(new DateTime(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc), IntervaloAgrupacion.UnaHora.InicioDe(instante));
    }
}

public class EsperaTrasAtenderTests
{
    private static readonly DateTime Ahora = new(2026, 11, 3, 14, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Espera = TimeSpan.FromMinutes(30);

    private static AlertaClinica Atendida(TipoAlerta tipo = TipoAlerta.Riesgo, PrioridadAlerta prioridad = PrioridadAlerta.Media, string? nivel = "medio")
    {
        var alerta = AlertaClinica.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), tipo, prioridad, nivel, 5, 2, null, Ahora.AddMinutes(-5));
        alerta.Atender(Guid.NewGuid(), "Se avisó al médico de guardia.", Ahora);
        return alerta;
    }

    [Fact]
    public void Durante_la_espera_el_mismo_nivel_no_abre_otra_alerta() =>
        Assert.True(Atendida().SilenciaNuevaAlerta(PrioridadAlerta.Media, "medio", Ahora.AddMinutes(29), Espera));

    [Fact]
    public void Durante_la_espera_un_nivel_menor_tampoco() =>
        Assert.True(Atendida().SilenciaNuevaAlerta(PrioridadAlerta.Baja, "bajo-medio", Ahora.AddMinutes(1), Espera));

    [Theory]
    [InlineData(PrioridadAlerta.Alta, "alto")]
    [InlineData(PrioridadAlerta.Media, "alto")]
    [InlineData(PrioridadAlerta.Alta, "medio")]
    public void Si_sube_la_prioridad_o_el_nivel_si_se_abre(PrioridadAlerta prioridad, string nivel) =>
        Assert.False(Atendida().SilenciaNuevaAlerta(prioridad, nivel, Ahora.AddMinutes(1), Espera));

    [Fact]
    public void Al_terminar_la_espera_se_abre_aunque_siga_en_el_mismo_nivel() =>
        Assert.False(Atendida().SilenciaNuevaAlerta(PrioridadAlerta.Media, "medio", Ahora.AddMinutes(30), Espera));

    [Fact]
    public void Solo_aplica_a_las_alertas_de_riesgo_atendidas()
    {
        Assert.False(Atendida(TipoAlerta.Caida, PrioridadAlerta.Alta, null).SilenciaNuevaAlerta(PrioridadAlerta.Alta, null, Ahora.AddMinutes(1), Espera));

        var abierta = AlertaClinica.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TipoAlerta.Riesgo, PrioridadAlerta.Media, "medio", 5, 2, null, Ahora);
        Assert.False(abierta.SilenciaNuevaAlerta(PrioridadAlerta.Media, "medio", Ahora.AddMinutes(1), Espera));
    }
}
