using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Vitalify.Infrastructure.Persistence.Historial;
using Vitalify.Infrastructure.Persistence.Transaccional;

namespace Vitalify.Infrastructure.Persistence;

/// <summary>
/// Fábricas usadas solo por <c>dotnet ef</c>. Cargan el <c>.env</c> de la raíz del repositorio igual que la API.
/// </summary>
internal static class ConfiguracionDeDiseno
{
    public static IConfiguration Cargar()
    {
        Env.TraversePath().Load();
        return new ConfigurationBuilder().AddEnvironmentVariables().Build();
    }
}

public class TransaccionalDbContextFactory : IDesignTimeDbContextFactory<TransaccionalDbContext>
{
    public TransaccionalDbContext CreateDbContext(string[] args)
    {
        var cadena = CadenaDeConexion.Obtener(ConfiguracionDeDiseno.Cargar(), CadenaDeConexion.Transaccional);
        var options = new DbContextOptionsBuilder<TransaccionalDbContext>();
        OpcionesNpgsql.Configurar(options, cadena, TransaccionalDbContext.Esquema);
        return new TransaccionalDbContext(options.Options);
    }
}

public class HistorialDbContextFactory : IDesignTimeDbContextFactory<HistorialDbContext>
{
    public HistorialDbContext CreateDbContext(string[] args)
    {
        var cadena = CadenaDeConexion.Obtener(ConfiguracionDeDiseno.Cargar(), CadenaDeConexion.Historial);
        var options = new DbContextOptionsBuilder<HistorialDbContext>();
        OpcionesNpgsql.Configurar(options, cadena, HistorialDbContext.Esquema);
        return new HistorialDbContext(options.Options);
    }
}
