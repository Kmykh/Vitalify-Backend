using Microsoft.Extensions.Configuration;
using Npgsql;
using Vitalify.Infrastructure.Persistence;

namespace Vitalify.Api.Tests.Persistencia;

/// <summary>Las dos cadenas de Supabase: un pool por contexto y, juntos, no más de 10 conexiones contra la misma base.</summary>
public sealed class CadenaDeConexionTests
{
    private const string Supabase = "Host=aws-0-us-west-2.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.ref;Password=x;SSL Mode=Require";

    private static IConfiguration Configuracion(string transaccional, string historial) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Transaccional"] = transaccional,
            ["ConnectionStrings:Historial"] = historial,
        }).Build();

    [Fact]
    public void Cada_contexto_tiene_su_pool_de_5_y_su_nombre_de_aplicacion()
    {
        var configuracion = Configuracion(Supabase, Supabase);

        var transaccional = new NpgsqlConnectionStringBuilder(CadenaDeConexion.Obtener(configuracion, CadenaDeConexion.Transaccional));
        var historial = new NpgsqlConnectionStringBuilder(CadenaDeConexion.Obtener(configuracion, CadenaDeConexion.Historial));

        Assert.Equal("vitalify-transaccional", transaccional.ApplicationName);
        Assert.Equal("vitalify-historial", historial.ApplicationName);
        Assert.Equal(5, transaccional.MaxPoolSize);
        Assert.Equal(5, historial.MaxPoolSize);
        Assert.Equal(SslMode.Require, transaccional.SslMode);
    }

    [Fact]
    public void Respeta_el_nombre_de_aplicacion_si_la_cadena_lo_fija()
    {
        var cadena = CadenaDeConexion.Obtener(Configuracion(Supabase + ";Application Name=otra", Supabase), CadenaDeConexion.Transaccional);

        Assert.Equal("otra", new NpgsqlConnectionStringBuilder(cadena).ApplicationName);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(3, 7)]
    public void Hasta_10_conexiones_contra_la_misma_base_es_valido(int poolTransaccional, int poolHistorial) =>
        CadenaDeConexion.ValidarPresupuesto(
            $"{Supabase};Maximum Pool Size={poolTransaccional}", $"{Supabase};Maximum Pool Size={poolHistorial}");

    [Fact]
    public void Mas_de_10_conexiones_contra_la_misma_base_no_arranca()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CadenaDeConexion.ValidarPresupuesto(
            $"{Supabase};Maximum Pool Size=6", $"{Supabase};Maximum Pool Size=5"));

        Assert.Contains("suman 11", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Si_el_historial_esta_en_otra_base_cada_una_tiene_su_propio_limite() =>
        CadenaDeConexion.ValidarPresupuesto(
            $"{Supabase};Maximum Pool Size=8", "Host=otra-base.example;Port=5432;Database=historial;Username=u;Password=x;Maximum Pool Size=8");
}
