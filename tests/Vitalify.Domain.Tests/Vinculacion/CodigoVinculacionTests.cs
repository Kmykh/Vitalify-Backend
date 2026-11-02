using Vitalify.Domain.Comun;
using Vitalify.Domain.Pacientes;
using Vitalify.Domain.Vinculacion;

namespace Vitalify.Domain.Tests.Vinculacion;

public class CodigoVinculacionTests
{
    private static readonly DateTime Ahora = new(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Diez = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Cinco = TimeSpan.FromMinutes(5);

    [Fact]
    public void Generar_produce_8_caracteres_del_alfabeto_y_guarda_solo_el_hash()
    {
        var (codigo, enClaro) = CodigoVinculacion.Generar(Guid.NewGuid(), Ahora, Diez);

        Assert.Equal(CodigoVinculacion.Largo, enClaro.Length);
        Assert.All(enClaro, c => Assert.Contains(c, CodigoVinculacion.Alfabeto));
        Assert.Matches("^[0-9a-f]{64}$", codigo.HashCodigo);
        Assert.DoesNotContain(enClaro, codigo.HashCodigo, StringComparison.OrdinalIgnoreCase);
        Assert.Equal((Ahora, Ahora + Diez, 0), (codigo.CreadoEn, codigo.ExpiraEn, codigo.IntentosFallidos));
        Assert.True(codigo.EstaActivo);
    }

    [Fact]
    public void El_alfabeto_no_tiene_caracteres_que_se_confunden_en_la_pantalla()
    {
        Assert.Equal(31, CodigoVinculacion.Alfabeto.Length);
        Assert.All("01ILO", c => Assert.DoesNotContain(c, CodigoVinculacion.Alfabeto));
    }

    [Fact]
    public void Los_codigos_son_aleatorios()
    {
        var codigos = Enumerable.Range(0, 500).Select(_ => CodigoVinculacion.Generar(Guid.NewGuid(), Ahora, Diez).EnClaro).ToHashSet();

        Assert.Equal(500, codigos.Count);
    }

    [Fact]
    public void Coincide_compara_el_hash_sin_distinguir_mayusculas()
    {
        var (codigo, enClaro) = CodigoVinculacion.Generar(Guid.NewGuid(), Ahora, Diez);

        Assert.True(codigo.Coincide(enClaro));
        Assert.True(codigo.Coincide($" {enClaro.ToLowerInvariant()} "));
        Assert.False(codigo.Coincide(enClaro == "22222222" ? "33333333" : "22222222"));
        Assert.False(codigo.Coincide(null));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Vence_a_los_10_minutos(int segundosDespuesDelVencimiento, bool vigente)
    {
        var (codigo, _) = CodigoVinculacion.Generar(Guid.NewGuid(), Ahora, Diez);

        Assert.Equal(vigente, codigo.EstaVigente(Ahora + Diez + TimeSpan.FromSeconds(segundosDespuesDelVencimiento)));
    }

    [Fact]
    public void Al_quinto_intento_fallido_el_codigo_se_invalida()
    {
        var (codigo, _) = CodigoVinculacion.Generar(Guid.NewGuid(), Ahora, Diez);

        for (var i = 1; i < 5; i++)
        {
            Assert.False(codigo.RegistrarIntentoFallido(Ahora, maximo: 5));
        }

        Assert.True(codigo.RegistrarIntentoFallido(Ahora, maximo: 5));
        Assert.Equal((5, (DateTime?)Ahora, false), (codigo.IntentosFallidos, codigo.InvalidadoEn, codigo.EstaActivo));
        Assert.Throws<ExcepcionDeDominio>(() => codigo.RegistrarIntentoFallido(Ahora, maximo: 5));
    }

    [Fact]
    public void El_token_se_emite_con_un_codigo_vigente_y_vale_5_minutos_una_sola_vez()
    {
        var (codigo, _) = CodigoVinculacion.Generar(Guid.NewGuid(), Ahora, Diez);

        var token = codigo.EmitirToken(Ahora.AddMinutes(1), Cinco);

        Assert.Equal(CodigoVinculacion.Hash(token), codigo.TokenHash);
        Assert.True(codigo.TokenCoincide(token, Ahora.AddMinutes(5)));
        Assert.False(codigo.TokenCoincide(token, Ahora.AddMinutes(6)));
        Assert.False(codigo.TokenCoincide("otro", Ahora.AddMinutes(2)));

        codigo.MarcarUsado(Ahora.AddMinutes(2));
        Assert.False(codigo.EstaActivo);
        Assert.False(codigo.TokenCoincide(token, Ahora.AddMinutes(3)));
        Assert.Throws<ExcepcionDeDominio>(() => codigo.MarcarUsado(Ahora.AddMinutes(3)));
    }

    [Fact]
    public void Un_segundo_escaneo_reemplaza_el_token_anterior()
    {
        var (codigo, _) = CodigoVinculacion.Generar(Guid.NewGuid(), Ahora, Diez);
        var primero = codigo.EmitirToken(Ahora, Cinco);

        var segundo = codigo.EmitirToken(Ahora.AddMinutes(1), Cinco);

        Assert.False(codigo.TokenCoincide(primero, Ahora.AddMinutes(1)));
        Assert.True(codigo.TokenCoincide(segundo, Ahora.AddMinutes(1)));
    }

    [Fact]
    public void No_se_emite_token_con_un_codigo_vencido_ni_invalidado()
    {
        var (vencido, _) = CodigoVinculacion.Generar(Guid.NewGuid(), Ahora, Diez);
        var (invalidado, _) = CodigoVinculacion.Generar(Guid.NewGuid(), Ahora, Diez);
        invalidado.Invalidar(Ahora);

        Assert.Throws<ExcepcionDeDominio>(() => vencido.EmitirToken(Ahora + Diez, Cinco));
        Assert.Throws<ExcepcionDeDominio>(() => invalidado.EmitirToken(Ahora, Cinco));
    }

    [Theory]
    [InlineData("K7P3QX9M", true)]
    [InlineData("k7p3qx9m", true)]
    [InlineData("K7P3QX9", false)]
    [InlineData("K7P3QX9MZ", false)]
    [InlineData("K7P3QX0M", false)]
    [InlineData("K7P3QXOM", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Formato_del_codigo(string? codigo, bool valido) => Assert.Equal(valido, CodigoVinculacion.FormatoValido(codigo));
}

public class FormatoQrTests
{
    [Fact]
    public void Compone_e_interpreta_el_contenido_del_qr()
    {
        var qr = FormatoQr.Componer("esp32-001", "k7p3qx9m");

        Assert.Equal("VTF1:ESP32-001:K7P3QX9M", qr);
        Assert.True(FormatoQr.TryInterpretar(qr, out var dispositivo, out var codigo));
        Assert.Equal(("ESP32-001", "K7P3QX9M"), (dispositivo, codigo));
    }

    [Fact]
    public void Con_el_codigo_de_dispositivo_mas_largo_entra_en_un_qr_version_3_alfanumerico()
    {
        var qr = FormatoQr.Componer(new string('A', 32), "K7P3QX9M");

        // Versión 3 con corrección M: 61 caracteres alfanuméricos.
        Assert.True(qr.Length <= 61);
        Assert.Matches("^[0-9A-Z $%*+\\-./:]+$", qr);
    }

    [Theory]
    [InlineData("hola")]
    [InlineData("VTF2:ESP32-001:K7P3QX9M")]
    [InlineData("VTF1:ESP32-001")]
    [InlineData("VTF1:ESP 32:K7P3QX9M")]
    [InlineData("VTF1:ESP32-001:K7P3QX9")]
    [InlineData("VTF1:ESP32-001:K7P3QX9M:extra")]
    [InlineData("")]
    [InlineData(null)]
    public void Rechaza_lo_que_no_tiene_el_formato(string? texto) => Assert.False(FormatoQr.TryInterpretar(texto, out _, out _));
}

public class InicialesTests
{
    [Theory]
    [InlineData("Rosa Mendoza Quispe", "R.M.")]
    [InlineData("Juan Pérez", "J.P.")]
    [InlineData("María de los Ángeles Pérez Rojas", "M.P.")]
    [InlineData("José Ñahui Ccori", "J.N.")]
    [InlineData("  ángel   úrsula  ", "A.U.")]
    [InlineData("Luis Alberto Torres Vega", "L.T.")]
    [InlineData("Ana", "A.")]
    [InlineData("", "")]
    public void Primera_letra_del_primer_nombre_y_del_primer_apellido(string nombre, string iniciales) =>
        Assert.Equal(iniciales, Paciente.CalcularIniciales(nombre));
}
