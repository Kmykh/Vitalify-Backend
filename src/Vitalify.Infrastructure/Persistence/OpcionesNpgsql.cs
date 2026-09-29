using Microsoft.EntityFrameworkCore;

namespace Vitalify.Infrastructure.Persistence;

/// <summary>
/// Configuración de Npgsql compartida por la API, las fábricas de diseño de <c>dotnet ef</c> y las pruebas de
/// integración. La tabla de historial de migraciones de cada contexto vive en el esquema de ese contexto.
/// </summary>
public static class OpcionesNpgsql
{
    public static DbContextOptionsBuilder Configurar(DbContextOptionsBuilder options, string cadena, string esquema) =>
        options.UseNpgsql(cadena, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", esquema));
}
